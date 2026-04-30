using System;
using System.Collections.Generic;
using FFmpeg.AutoGen;
using SIPSorceryMedia.FFmpeg;
using WinSender.Diagnostics;

namespace WinSender.WebRTC;

/// <summary>
/// Probes available H.264 encoders by ACTUALLY opening them via avcodec_open2.
/// Two-stage detection:
///   1) avcodec_find_encoder_by_name → confirms encoder symbol is registered in this FFmpeg build.
///   2) avcodec_alloc_context3 + avcodec_open2 → confirms driver/runtime (NVENC/QSV/AMF) is actually present.
/// </summary>
public static class HardwareEncoderDetector
{
    public sealed record EncoderChoice(
        string EncoderName,
        Dictionary<string, string> CodecOptions,
        bool IsHardware);

    /// <summary>Vendor token → encoder name.</summary>
    private static readonly Dictionary<string, string> VendorEncoder = new()
    {
        ["nvidia"] = "h264_nvenc",
        ["intel"]  = "h264_qsv",
        ["amd"]    = "h264_amf",
    };

    private static readonly object _initLock = new();
    private static bool _ffmpegInitialised;

    /// <summary>
    /// Probe which vendors are actually usable on this machine.
    /// Returns ordered list including "auto" first, then any vendor whose encoder
    /// successfully passes avcodec_open2. Always at least ["auto"].
    /// Safe to call multiple times — idempotent.
    /// </summary>
    public static List<string> ProbeAvailableVendors()
    {
        EnsureFFmpegInitialised();

        var result = new List<string> { "auto" };
        foreach (var (vendor, encoderName) in VendorEncoder)
        {
            if (CanOpenEncoder(encoderName, out var detail))
            {
                Console.WriteLine($"[EncoderProbe] {vendor} ({encoderName}) → AVAILABLE");
                result.Add(vendor);
            }
            else
            {
                Console.WriteLine($"[EncoderProbe] {vendor} ({encoderName}) → unavailable: {detail}");
            }
        }
        return result;
    }

    /// <summary>
    /// Detect the best available H.264 encoder for the requested vendor.
    /// HwAccel=false → VP8 path (caller decides; we still return libx264 in case caller wants software H264).
    /// Auto fallback: nvenc → qsv → amf → libx264.
    /// </summary>
    public static EncoderChoice Detect(bool hwAccel, string vendor, int targetBitrateKbps = 4000, int targetFps = 30)
    {
        EnsureFFmpegInitialised();

        if (!hwAccel)
        {
            Console.WriteLine("[EncoderDetect] HwAccel disabled → libx264");
            return new EncoderChoice("libx264", BuildSoftwareOptions(targetBitrateKbps, targetFps), false);
        }

        var candidates = vendor switch
        {
            "nvidia" => new[] { "h264_nvenc" },
            "intel"  => new[] { "h264_qsv" },
            "amd"    => new[] { "h264_amf" },
            _        => new[] { "h264_nvenc", "h264_qsv", "h264_amf" }, // auto
        };

        foreach (var name in candidates)
        {
            if (CanOpenEncoder(name, out var detail))
            {
                AppLog.For("EncoderDetect").Information("Selected hardware encoder={Encoder} vendor={Vendor}", name, vendor);
                return new EncoderChoice(name, BuildHardwareOptions(name, targetBitrateKbps, targetFps), true);
            }
            else
            {
                AppLog.For("EncoderDetect").Warning("Encoder unavailable encoder={Encoder} detail={Detail}", name, detail);
            }
        }

        AppLog.For("EncoderDetect").Warning("No hardware encoder usable vendor={Vendor} → libx264", vendor);
        return new EncoderChoice("libx264", BuildSoftwareOptions(targetBitrateKbps, targetFps), false);
    }

    /// <summary>
    /// Real probe: find encoder symbol, allocate context with minimal valid params,
    /// call avcodec_open2. Free everything. Returns true only if open succeeded.
    /// </summary>
    private static unsafe bool CanOpenEncoder(string encoderName, out string detail)
    {
        detail = string.Empty;
        AVCodecContext* ctx = null;
        try
        {
            var codec = ffmpeg.avcodec_find_encoder_by_name(encoderName);
            if (codec == null)
            {
                detail = "encoder symbol not found in this FFmpeg build";
                return false;
            }

            ctx = ffmpeg.avcodec_alloc_context3(codec);
            if (ctx == null)
            {
                detail = "avcodec_alloc_context3 returned null";
                return false;
            }

            // Minimal valid params — must satisfy each encoder's basic requirements.
            ctx->width = 1280;
            ctx->height = 720;
            ctx->time_base = new AVRational { num = 1, den = 30 };
            ctx->framerate = new AVRational { num = 30, den = 1 };
            ctx->pix_fmt = PickPixFmt(encoderName);
            ctx->bit_rate = 2_000_000;
            ctx->gop_size = 60;
            ctx->max_b_frames = 0;

            // Match real-run constraints so probe success implies real success.
            // h264_amf: ultralowlatency usage has stricter validation than default transcoding;
            // probing without these can produce false-positive (probe OK, real run fails).
            AVDictionary* probeOpts = null;
            if (encoderName == "h264_amf")
            {
                ffmpeg.av_dict_set(&probeOpts, "usage", "ultralowlatency", 0);
                ffmpeg.av_dict_set(&probeOpts, "rc", "cbr", 0);
                ffmpeg.av_dict_set(&probeOpts, "profile", "constrained_baseline", 0);
                ffmpeg.av_dict_set(&probeOpts, "bf", "0", 0);
            }

            int ret = ffmpeg.avcodec_open2(ctx, codec, &probeOpts);
            if (probeOpts != null) ffmpeg.av_dict_free(&probeOpts);
            if (ret < 0)
            {
                detail = $"avcodec_open2 failed code={ret} ({DecodeError(ret)})";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            detail = $"exception: {ex.Message}";
            return false;
        }
        finally
        {
            if (ctx != null)
            {
                try { ffmpeg.avcodec_free_context(&ctx); } catch { }
            }
        }
    }

    private static AVPixelFormat PickPixFmt(string encoderName) => encoderName switch
    {
        // Hardware encoders accept NV12 as input via system memory path (no hw_device_ctx needed for probe).
        "h264_nvenc" => AVPixelFormat.AV_PIX_FMT_NV12,
        "h264_qsv"   => AVPixelFormat.AV_PIX_FMT_NV12,
        "h264_amf"   => AVPixelFormat.AV_PIX_FMT_NV12,
        _            => AVPixelFormat.AV_PIX_FMT_YUV420P,
    };

    private static unsafe string DecodeError(int code)
    {
        const int bufSize = 256;
        var buf = stackalloc byte[bufSize];
        ffmpeg.av_strerror(code, buf, (ulong)bufSize);
        return System.Text.Encoding.UTF8.GetString(buf, bufSize).TrimEnd('\0');
    }

    private static void EnsureFFmpegInitialised()
    {
        lock (_initLock)
        {
            if (_ffmpegInitialised) return;
            try
            {
                var libDir = AppContext.BaseDirectory;
                FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL, libPath: libDir);
                _ffmpegInitialised = true;
                Console.WriteLine($"[EncoderProbe] FFmpeg initialised from {libDir}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EncoderProbe] FFmpeg init failed: {ex.Message}");
                _ffmpegInitialised = true; // don't keep retrying
            }
        }
    }

    private static Dictionary<string, string> BuildHardwareOptions(string encoderName, int bitrateKbps, int fps)
    {
        var bitrate = (bitrateKbps * 1000).ToString();
        var maxrate = ((int)(bitrateKbps * 1.5) * 1000).ToString();
        var bufsize = ((bitrateKbps * 2) * 1000).ToString();
        var gop = (fps * 2).ToString(); // keyframe every 2s

        return encoderName switch
        {
            // NVENC: p1=fastest preset (best for low-latency screen mirroring)
            // tune=ull (ultra-low-latency), forced-idr=1 ensures keyframes on demand
            "h264_nvenc" => new Dictionary<string, string>
            {
                ["preset"]      = "p1",
                ["tune"]        = "ull",
                ["rc"]          = "cbr",
                ["zerolatency"] = "1",
                ["delay"]       = "0",
                ["forced-idr"]  = "1",
                ["bf"]          = "0",
                ["b"]           = bitrate,
                ["maxrate"]     = maxrate,
                ["bufsize"]     = bufsize,
                ["g"]           = gop,
                ["profile"]     = "baseline",
            },
            // QSV: async_depth=1 minimises pipeline buffer; low_power=1 forces VDEnc (fastest HW path on Intel iGPU)
            "h264_qsv" => new Dictionary<string, string>
            {
                ["preset"]      = "veryfast",
                ["low_delay"]   = "1",
                ["adaptive_i"]  = "0",
                ["adaptive_b"]  = "0",
                ["async_depth"] = "1",
                ["look_ahead"]  = "0",
                ["low_power"]   = "1",
                ["b"]           = bitrate,
                ["maxrate"]     = maxrate,
                ["bufsize"]     = bufsize,
                ["g"]           = gop,
                ["profile"]     = "baseline",
                ["idr_interval"] = "0",
            },
            // AMF: ultralowlatency usage, low_latency_mode for FFmpeg 7.x
            "h264_amf" => new Dictionary<string, string>
            {
                ["usage"]            = "ultralowlatency",
                ["quality"]          = "speed",
                ["low_latency_mode"] = "1",
                ["rc"]               = "cbr",
                ["enforce_hrd"]      = "1",
                ["latency"]          = "1",
                ["b"]                = bitrate,
                ["maxrate"]          = maxrate,
                ["bufsize"]          = bufsize,
                ["g"]                = gop,
                ["bf"]               = "0",
                ["profile"]          = "constrained_baseline",
                ["forced_idr"]       = "1",
                ["header_spacing"]   = "1",
                ["aud"]              = "1",
            },
            _ => new Dictionary<string, string>(),
        };
    }

    private static Dictionary<string, string> BuildSoftwareOptions(int bitrateKbps, int fps)
    {
        return new Dictionary<string, string>
        {
            ["preset"]  = "ultrafast",
            ["tune"]    = "zerolatency",
            ["bf"]      = "0",
            ["b"]       = (bitrateKbps * 1000).ToString(),
            ["maxrate"] = ((int)(bitrateKbps * 1.5) * 1000).ToString(),
            ["bufsize"] = ((bitrateKbps * 2) * 1000).ToString(),
            ["g"]       = (fps * 2).ToString(),
            ["profile"] = "baseline",
        };
    }
}

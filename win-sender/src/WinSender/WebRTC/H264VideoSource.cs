using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;

namespace WinSender.WebRTC;

internal static class FfmpegRetExtensions
{
    public static int EnsureOk(this int code, string op)
    {
        if (code < 0) throw new InvalidOperationException($"{op} failed code={code}");
        return code;
    }
}

/// <summary>
/// Native H.264 video source that bypasses SIPSorceryMedia.FFmpeg.FFmpegVideoEncoder.
///
/// Why: the upstream FFmpegVideoEncoder calls avcodec_receive_packet() ONCE after each
/// avcodec_send_frame(). NVENC/QSV/AMF are async pipelines — first ~N frames return EAGAIN
/// from receive_packet, AND any send_frame after the encoder fills its internal queue also
/// returns EAGAIN (which upstream throws as exception → caller drops frame).
///
/// Result with upstream: only the first frame ever made it through, all subsequent frames
/// hit EAGAIN exception → "TV shows frame 1 then freezes".
///
/// This implementation:
///   • Drains receive_packet in a loop until EAGAIN (proper async pattern).
///   • Treats send_frame EAGAIN as "drain first, then resend" instead of exception.
///   • Concatenates all NAL units of one access unit + injects extradata (SPS/PPS) before
///     every keyframe so SIPSorcery's H264 packetiser can split & RTP-pack correctly.
///   • Honours ForceKeyFrame() between frames.
/// </summary>
public sealed unsafe class H264VideoSource : IVideoSource, IDisposable
{
    private readonly string _encoderName;
    private readonly Dictionary<string, string> _codecOptions;
    private readonly int _targetFps;
    private readonly VideoFormat _h264Format;
    private readonly List<VideoFormat> _supportedFormats;

    private AVCodec* _codec;
    private AVCodecContext* _ctx;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private SwsContext* _sws;
    private int _swsWidth, _swsHeight;
    private byte[] _nv12Buffer     = Array.Empty<byte>(); // pre-allocated NV12 staging buffer (BGRA fast-path)
    private byte[] _prevBgraBuffer = Array.Empty<byte>();
    private byte[] _prevNv12Buffer = Array.Empty<byte>();
    private byte[] _extradata = Array.Empty<byte>();  // Annex-B SPS/PPS prefix

    private readonly object _encLock = new();
    private bool _initialised;
    private bool _started;
    private bool _paused;
    private bool _forceKeyFrame = true; // first frame must be keyframe
    private bool _disposed;
    private long _pts;

    public string EncoderName => _encoderName;

    public event EncodedSampleDelegate? OnVideoSourceEncodedSample;
    public event RawVideoSampleDelegate? OnVideoSourceRawSample { add { } remove { } }
    public event RawVideoSampleFasterDelegate? OnVideoSourceRawSampleFaster { add { } remove { } }
    public event SourceErrorDelegate? OnVideoSourceError { add { } remove { } }

    public H264VideoSource(string encoderName, Dictionary<string, string> codecOptions, int targetFps = 60)
    {
        _encoderName = encoderName;
        _codecOptions = codecOptions;
        _targetFps = targetFps;
        _h264Format = new VideoFormat(VideoCodecsEnum.H264, 96, 90000, "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f");
        _supportedFormats = new List<VideoFormat> { _h264Format };
        Console.WriteLine($"[H264VideoSource] Constructed encoder={encoderName}");
    }

    public List<VideoFormat> GetVideoSourceFormats() => _supportedFormats;
    public void SetVideoSourceFormat(VideoFormat videoFormat) { }
    public void RestrictFormats(Func<VideoFormat, bool> filter) { }
    public Task StartVideo() { _started = true; return Task.CompletedTask; }
    public Task PauseVideo()  { _paused  = true; return Task.CompletedTask; }
    public Task ResumeVideo() { _paused  = false; return Task.CompletedTask; }
    public Task CloseVideo()  { _started = false; return Task.CompletedTask; }
    public void ForceKeyFrame() => _forceKeyFrame = true;
    public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
    public bool IsVideoSourcePaused() => _paused;

    public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        if (!_started || _paused || _disposed) return;
        if (sample == null || sample.Length == 0) return;
        if (OnVideoSourceEncodedSample == null) return;
        if (pixelFormat != VideoPixelFormatsEnum.NV12 &&
            pixelFormat != VideoPixelFormatsEnum.Bgra &&
            pixelFormat != VideoPixelFormatsEnum.Rgba &&
            pixelFormat != VideoPixelFormatsEnum.Bgr)
        {
            Console.WriteLine($"[H264VideoSource] Unsupported pixel format {pixelFormat}");
            return;
        }

        try
        {
            lock (_encLock)
            {
                if (!_initialised)
                {
                    InitialiseEncoder(width, height);
                    _initialised = true;
                }

                bool keyFrame = _forceKeyFrame;
                _forceKeyFrame = false;

                if (pixelFormat == VideoPixelFormatsEnum.NV12)
                    EncodeAndEmitNV12(sample, width, height, keyFrame, durationMilliseconds);
                else
                    EncodeAndEmitConvert(sample, width, height, keyFrame, durationMilliseconds, pixelFormat);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[H264VideoSource] Encode failed: {ex.Message}");
        }
    }

    private void EncodeAndEmitNV12(byte[] nv12, int width, int height, bool keyFrame, uint durationMs)
    {
        int ySize = width * height;
        int uvSize = ySize / 2;
        if (nv12.Length < ySize + uvSize)
        {
            Console.WriteLine($"[H264VideoSource] NV12 buffer too small: {nv12.Length} < {ySize + uvSize}");
            return;
        }

        ffmpeg.av_frame_make_writable(_frame).EnsureOk("av_frame_make_writable");
        Marshal.Copy(nv12, 0,        (IntPtr)_frame->data[0], ySize);
        Marshal.Copy(nv12, ySize,    (IntPtr)_frame->data[1], uvSize);

        SubmitFrame(width, height, keyFrame, durationMs);
    }

    private void EncodeAndEmitConvert(byte[] src, int width, int height, bool keyFrame, uint durationMs, VideoPixelFormatsEnum pixelFormat)
    {
        if (pixelFormat == VideoPixelFormatsEnum.Bgra)
        {
            int nv12Size  = width * height * 3 / 2;
            int bgraSize  = width * height * 4;
            if (_nv12Buffer.Length     < nv12Size)  _nv12Buffer     = new byte[nv12Size];
            if (_prevBgraBuffer.Length < bgraSize)  _prevBgraBuffer = new byte[bgraSize];
            if (_prevNv12Buffer.Length < nv12Size)  _prevNv12Buffer = new byte[nv12Size];

            bool hasPrev = !keyFrame && _prevNv12Buffer.Length == nv12Size && _prevBgraBuffer.Length == bgraSize;
            if (hasPrev)
                BgraToNv12Converter.Convert(src, _prevBgraBuffer, _prevNv12Buffer, width, height, _nv12Buffer);
            else
                BgraToNv12Converter.Convert(src, width, height, _nv12Buffer);

            Array.Copy(src,         _prevBgraBuffer, bgraSize);
            Array.Copy(_nv12Buffer, _prevNv12Buffer, nv12Size);

            EncodeAndEmitNV12(_nv12Buffer, width, height, keyFrame, durationMs);
            return;
        }

        AVPixelFormat srcFmt = pixelFormat switch
        {
            VideoPixelFormatsEnum.Bgra => AVPixelFormat.AV_PIX_FMT_BGRA,
            VideoPixelFormatsEnum.Rgba => AVPixelFormat.AV_PIX_FMT_RGBA,
            VideoPixelFormatsEnum.Bgr  => AVPixelFormat.AV_PIX_FMT_BGR24,
            _ => AVPixelFormat.AV_PIX_FMT_BGRA,
        };

        if (_sws == null || _swsWidth != width || _swsHeight != height)
        {
            if (_sws != null) { ffmpeg.sws_freeContext(_sws); _sws = null; }
            _sws = ffmpeg.sws_getContext(
                width, height, srcFmt,
                width, height, AVPixelFormat.AV_PIX_FMT_NV12,
                ffmpeg.SWS_FAST_BILINEAR, null, null, null);
            if (_sws == null) throw new InvalidOperationException("sws_getContext failed");
            _swsWidth = width; _swsHeight = height;
        }

        ffmpeg.av_frame_make_writable(_frame).EnsureOk("av_frame_make_writable");

        int bytesPerPixel = srcFmt == AVPixelFormat.AV_PIX_FMT_BGR24 ? 3 : 4;
        int srcStride = width * bytesPerPixel;
        if (src.Length < srcStride * height)
        {
            Console.WriteLine($"[H264VideoSource] src buffer too small: {src.Length} < {srcStride * height}");
            return;
        }

        // Pin managed src buffer; build src slice arrays for sws_scale.
        var handle = GCHandle.Alloc(src, GCHandleType.Pinned);
        try
        {
            byte_ptrArray8 srcData = default;
            srcData[0] = (byte*)handle.AddrOfPinnedObject();
            int_array8 srcLineSize = default;
            srcLineSize[0] = srcStride;

            int sliced = ffmpeg.sws_scale(_sws, srcData, srcLineSize, 0, height,
                                          _frame->data, _frame->linesize);
            if (sliced <= 0)
            {
                Console.WriteLine($"[H264VideoSource] sws_scale returned {sliced}");
                return;
            }
        }
        finally { handle.Free(); }

        SubmitFrame(width, height, keyFrame, durationMs);
    }

    private void SubmitFrame(int width, int height, bool keyFrame, uint durationMs)
    {
        _frame->pts = _pts++;
        _frame->width = width;
        _frame->height = height;
        _frame->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
        if (keyFrame)
        {
            _frame->flags |= ffmpeg.AV_FRAME_FLAG_KEY;
            _frame->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
        }
        else
        {
            _frame->pict_type = AVPictureType.AV_PICTURE_TYPE_NONE;
        }

        // FFmpeg async API: send_frame may EAGAIN when encoder queue is full;
        // the contract says "drain receive_packet first, then resend".
        int sendRet = ffmpeg.avcodec_send_frame(_ctx, _frame);
        if (sendRet == ffmpeg.AVERROR(ffmpeg.EAGAIN))
        {
            DrainPackets(durationMs, isKeyAccessUnit: keyFrame);
            sendRet = ffmpeg.avcodec_send_frame(_ctx, _frame);
        }
        if (sendRet < 0 && sendRet != ffmpeg.AVERROR_EOF)
        {
            Console.WriteLine($"[H264VideoSource] send_frame error code={sendRet}");
            return;
        }

        DrainPackets(durationMs, isKeyAccessUnit: keyFrame);
    }

    private void DrainPackets(uint durationMs, bool isKeyAccessUnit)
    {
        // Concatenate all NAL units belonging to the same access unit (= one source frame).
        using var ms = new MemoryStream();
        bool gotAny = false;

        while (true)
        {
            int ret = ffmpeg.avcodec_receive_packet(_ctx, _packet);
            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) break;
            if (ret < 0)
            {
                Console.WriteLine($"[H264VideoSource] receive_packet error code={ret}");
                break;
            }

            try
            {
                bool isKey = (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                // Inject SPS/PPS (extradata) before every keyframe — required because
                // most hardware encoders don't emit them inline by default.
                if (isKey && _extradata.Length > 0)
                {
                    ms.Write(_extradata, 0, _extradata.Length);
                }

                int size = _packet->size;
                if (size > 0)
                {
                    var managed = new byte[size];
                    Marshal.Copy((IntPtr)_packet->data, managed, 0, size);
                    ms.Write(managed, 0, size);
                    gotAny = true;
                }
            }
            finally
            {
                ffmpeg.av_packet_unref(_packet);
            }
        }

        if (gotAny)
        {
            uint durationRtpUnits = durationMs * 90;
            OnVideoSourceEncodedSample?.Invoke(durationRtpUnits, ms.ToArray());
        }
    }

    private void InitialiseEncoder(int width, int height)
    {
        _codec = ffmpeg.avcodec_find_encoder_by_name(_encoderName);
        if (_codec == null) throw new InvalidOperationException($"encoder {_encoderName} not found");

        _ctx = ffmpeg.avcodec_alloc_context3(_codec);
        if (_ctx == null) throw new InvalidOperationException("avcodec_alloc_context3 failed");

        _ctx->width = width;
        _ctx->height = height;
        _ctx->time_base = new AVRational { num = 1, den = _targetFps };
        _ctx->framerate = new AVRational { num = _targetFps, den = 1 };
        _ctx->pix_fmt = AVPixelFormat.AV_PIX_FMT_NV12;
        _ctx->gop_size = _targetFps * 2;
        _ctx->max_b_frames = 0;
        _ctx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
        // Explicit BT.709 limited-range colorimetry — AMF/QSV otherwise default to UNKNOWN
        // which causes washed-out colors on Android receivers (Issue #11 audit).
        _ctx->color_range     = AVColorRange.AVCOL_RANGE_MPEG;
        _ctx->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
        _ctx->color_trc       = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
        _ctx->colorspace      = AVColorSpace.AVCOL_SPC_BT709;

        // Apply codec options dictionary.
        AVDictionary* opts = null;
        try
        {
            foreach (var kv in _codecOptions)
            {
                ffmpeg.av_dict_set(&opts, kv.Key, kv.Value, 0);
                if (kv.Key == "b"       && long.TryParse(kv.Value, out var br))   _ctx->bit_rate        = br;
                if (kv.Key == "g"       && int.TryParse (kv.Value, out var gop))  _ctx->gop_size        = gop;
                // h264_amf reads rc_buffer_size / rc_max_rate from AVCodecContext fields, not dict
                // (Issue #5 audit). Mirror dict values into avctx so VBV/HRD path activates.
                if (kv.Key == "maxrate" && long.TryParse(kv.Value, out var mr))   _ctx->rc_max_rate     = mr;
                if (kv.Key == "bufsize" && int.TryParse (kv.Value, out var bs))   _ctx->rc_buffer_size  = bs;
                if (kv.Key == "bf"      && int.TryParse (kv.Value, out var bf))   _ctx->max_b_frames    = bf;
            }

            int ret = ffmpeg.avcodec_open2(_ctx, _codec, &opts);
            if (ret < 0) throw new InvalidOperationException($"avcodec_open2 failed code={ret}");
        }
        finally
        {
            if (opts != null) ffmpeg.av_dict_free(&opts);
        }

        // Capture extradata (SPS/PPS in Annex-B) for keyframe injection.
        if (_ctx->extradata != null && _ctx->extradata_size > 0)
        {
            _extradata = new byte[_ctx->extradata_size];
            Marshal.Copy((IntPtr)_ctx->extradata, _extradata, 0, _ctx->extradata_size);
            // Some encoders emit AVCC-style extradata (starts with 0x01). Convert to Annex-B if so.
            if (_extradata.Length > 0 && _extradata[0] == 0x01)
            {
                _extradata = ConvertAvccExtradataToAnnexB(_extradata);
            }
            Console.WriteLine($"[H264VideoSource] extradata captured: {_extradata.Length} bytes");
        }
        else
        {
            Console.WriteLine("[H264VideoSource] no extradata — encoder will inline SPS/PPS");
        }

        _frame = ffmpeg.av_frame_alloc();
        _frame->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
        _frame->width = width;
        _frame->height = height;
        ffmpeg.av_frame_get_buffer(_frame, 32).EnsureOk("av_frame_get_buffer");

        _packet = ffmpeg.av_packet_alloc();
        Console.WriteLine($"[H264VideoSource] encoder initialised: {_encoderName} {width}x{height}");
    }

    /// <summary>
    /// Convert AVCC extradata (avcC box: [version=1][...][numSPS][lenSPS][SPS][numPPS][lenPPS][PPS])
    /// to Annex-B (00 00 00 01 SPS 00 00 00 01 PPS).
    /// Best-effort — if parsing fails we return original bytes.
    /// </summary>
    private static byte[] ConvertAvccExtradataToAnnexB(byte[] avcc)
    {
        try
        {
            using var ms = new MemoryStream();
            int p = 5; // skip configurationVersion(1) + AVCProfileIndication(1) + profile_compat(1) + AVCLevelIndication(1) + lengthSizeMinusOne(1)
            int numSps = avcc[p++] & 0x1F;
            for (int i = 0; i < numSps; i++)
            {
                int len = (avcc[p] << 8) | avcc[p + 1]; p += 2;
                ms.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);
                ms.Write(avcc, p, len); p += len;
            }
            int numPps = avcc[p++];
            for (int i = 0; i < numPps; i++)
            {
                int len = (avcc[p] << 8) | avcc[p + 1]; p += 2;
                ms.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);
                ms.Write(avcc, p, len); p += len;
            }
            return ms.ToArray();
        }
        catch
        {
            return avcc;
        }
    }

    public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_encLock)
        {
            if (_packet != null) { var p = _packet; ffmpeg.av_packet_free(&p); _packet = null; }
            if (_frame  != null) { var f = _frame;  ffmpeg.av_frame_free(&f);  _frame  = null; }
            if (_ctx    != null) { var c = _ctx;    ffmpeg.avcodec_free_context(&c); _ctx = null; }
            if (_sws    != null) { ffmpeg.sws_freeContext(_sws); _sws = null; }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;
using SIPSorceryMedia.FFmpeg;
using WinSender.Abr;
using WinSender.Audio;
using WinSender.Capture;
using WinSender.Diagnostics;
using WinSender.Settings;
using WinSender.Signaling;

namespace WinSender.WebRTC;

public class WebRtcSender
{
    private RTCPeerConnection? _peerConnection;
    private readonly SignalingClient _signalingClient;
    private readonly ScreenCapture _screenCapture;
    private readonly SystemAudioCapture _audioCapture;
    private readonly AbrController _abrController;
    private CancellationTokenSource? _captureCts;
    private IVideoSource? _videoSource;
    private H264VideoSource? _h264Source;
    private VideoEncoderEndPoint? _vp8EndPoint;
    // Reserved for future telemetry over data channel; assigned when channel opens.
    private RTCDataChannel? _telemetryChannel = null;
    private int _lostFired = 0;
    private static bool _ffmpegInitialised = false;
    private static readonly object _ffmpegInitLock = new();

    private long _latestLatencyMs = -1;
    private string _encoderLabel = "VP8";
    private double _measuredFps = 0.0;
    private double _measuredMbps = 0.0;
    private long _encodedBytesWindow = 0;
    private const int TargetFps = 60;

    public long LatestLatencyMs => Interlocked.Read(ref _latestLatencyMs);
    public string EncoderLabel => _encoderLabel;
    public double MeasuredFps => _measuredFps;

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);

    public event Action<string>? ConnectionLost;

    public WebRtcSender(SignalingClient signalingClient, ScreenCapture screenCapture, SystemAudioCapture audioCapture, AbrController abrController)
    {
        _signalingClient = signalingClient;
        _screenCapture = screenCapture;
        _audioCapture = audioCapture;
        _abrController = abrController;

        _abrController.QualityChanged += (_, profile) =>
        {
            Console.WriteLine($"[WebRTC] ABR quality change: {profile.Width}x{profile.Height}@{profile.Fps}fps {profile.TargetBitrateKbps}kbps");
            AppLog.Abr("change", profile.Width, profile.Height, profile.Fps, profile.TargetBitrateKbps);
        };
    }

    public void SetSimulateBandwidth(int kbps)
    {
        _abrController.SetSimulateBandwidth(kbps);
    }

    public async Task StartAsync(string pairingCode)
    {
        var config = new RTCConfiguration
        {
            iceServers = new System.Collections.Generic.List<RTCIceServer>()
        };

        _peerConnection = new RTCPeerConnection(config);

        var settings = EncoderSettings.Load();
        Console.WriteLine($"[WebRTC] EncoderSettings: HwAccel={settings.HwAccel}, Vendor={settings.Vendor}");

        _videoSource = CreateVideoSource(settings);

        _telemetryChannel = await _peerConnection.createDataChannel("telemetry", new RTCDataChannelInit
        {
            ordered = true
        });
        if (_telemetryChannel != null)
        {
            _telemetryChannel.onopen += () => Console.WriteLine("[WebRTC] DataChannel 'telemetry' OPEN");
            _telemetryChannel.onclose += () => Console.WriteLine("[WebRTC] DataChannel 'telemetry' CLOSED");
            _telemetryChannel.onmessage += (ch, proto, data) =>
            {
                try
                {
                    var json = System.Text.Encoding.UTF8.GetString(data);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("latency_ms", out var lat))
                    {
                        Interlocked.Exchange(ref _latestLatencyMs, lat.GetInt64());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WebRTC] Telemetry parse error: {ex.Message}");
                }
            };
        }

        var supportedFormats = _videoSource.GetVideoSourceFormats();
        Console.WriteLine($"[WebRTC] Sender supported codecs: {string.Join(",", supportedFormats.ConvertAll(f => f.Codec.ToString()))}");
        var videoTrack = new MediaStreamTrack(supportedFormats, MediaStreamStatusEnum.SendOnly);
        _peerConnection.addTrack(videoTrack);
        int encodedCount = 0;
        _videoSource.OnVideoSourceEncodedSample += (durationRtpUnits, sample) =>
        {
            encodedCount++;
            if (encodedCount <= 5 || encodedCount % 60 == 0)
                Console.WriteLine($"[WebRTC] encoded sample #{encodedCount}, bytes={sample?.Length ?? 0}, dur={durationRtpUnits}");
            if (sample != null)
                Interlocked.Add(ref _encodedBytesWindow, sample.Length);
            _peerConnection?.SendVideo(durationRtpUnits, sample);
        };
        _peerConnection.OnVideoFormatsNegotiated += (formats) =>
        {
            Console.WriteLine($"[WebRTC] negotiated formats count={formats.Count}: {string.Join(",", formats.ConvertAll(f => f.Codec.ToString() + "/" + f.FormatID))}");
            if (formats.Count > 0) {
                _videoSource.SetVideoSourceFormat(formats[0]);
                Console.WriteLine($"[WebRTC] Using codec: {formats[0].Codec} formatID={formats[0].FormatID}");
            } else {
                Console.WriteLine($"[WebRTC] WARNING: No formats negotiated - video will not flow!");
            }
        };

        _peerConnection.onicecandidate += (candidate) =>
        {
            // Use Android-compatible field names: sdp, sdpMid, sdpMLineIndex
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                sdp = candidate.candidate,
                sdpMid = candidate.sdpMid,
                sdpMLineIndex = candidate.sdpMLineIndex
            });
            _ = _signalingClient.SendAsync(new SignalingMessage("ICE_CANDIDATE", payload));
        };

        _peerConnection.onconnectionstatechange += (state) =>
        {
            Console.WriteLine($"[WebRTC] Connection state: {state}");
            AppLog.PcState(state.ToString());
            if (state == RTCPeerConnectionState.connected)
            {
                AppLog.Connection("CONNECTED", _signalingClient.RemoteEndpointDescription ?? "-");
                SubscribeRtcpRtt(_peerConnection);
                _ = _videoSource!.StartVideo();
                _ = StartCaptureLoopAsync();
            }
            else if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected)
            {
                AppLog.Disconnect(_signalingClient.RemoteEndpointDescription ?? "-", state.ToString());
                _captureCts?.Cancel();
                if (Interlocked.Exchange(ref _lostFired, 1) == 0)
                {
                    try { ConnectionLost?.Invoke(state.ToString()); } catch { }
                }
            }
        };

        _signalingClient.MessageReceived += (msg) =>
        {
            // async void via lambda would let exceptions escape into ReceiveLoop and crash it.
            _ = Task.Run(async () =>
            {
                try
                {
                    if (msg.Type == "ANSWER" && msg.Payload != null)
                        await HandleAnswerAsync(msg.Payload);
                    else if (msg.Type == "ICE_CANDIDATE" && msg.Payload != null)
                        HandleIceCandidate(msg.Payload);
                }
                catch (Exception ex)
                {
                    AppLog.For("WebRTC").Error(ex, "MessageReceived handler failed type={Type}", msg.Type);
                }
            });
        };

        Console.WriteLine($"[WebRTC] Creating offer...");
        var offer = _peerConnection.createOffer();
        Console.WriteLine($"[WebRTC] Offer created, sdp.length={offer.sdp?.Length ?? 0}");
        // SIPSorcery bug: m=application section missing a=rtcp-mux, libwebrtc rejects answer.
        // RFC 8841 §5.1 requires it. Inject before setLocalDescription.
        offer.sdp = InjectRtcpMuxIntoApplicationSection(offer.sdp ?? string.Empty);
        await _peerConnection.setLocalDescription(offer);
        Console.WriteLine($"[WebRTC] setLocalDescription done; sending OFFER...");
        Console.WriteLine($"[WebRTC] === OFFER SDP ===\n{offer.sdp}\n=== END OFFER ===");
        await _signalingClient.SendAsync(new SignalingMessage("OFFER", offer.sdp));
    }

    private static string InjectRtcpMuxIntoApplicationSection(string sdp)
    {
        if (string.IsNullOrEmpty(sdp) || !sdp.Contains("m=application")) return sdp;
        // Locate application section bounds.
        int appIdx = sdp.IndexOf("m=application", StringComparison.Ordinal);
        int nextMIdx = sdp.IndexOf("\nm=", appIdx + 1, StringComparison.Ordinal);
        int endIdx = nextMIdx < 0 ? sdp.Length : nextMIdx;
        string appSection = sdp.Substring(appIdx, endIdx - appIdx);
        if (appSection.Contains("a=rtcp-mux"))
        {
            Console.WriteLine("[WebRTC] SDP application section already has rtcp-mux");
            return sdp;
        }
        // Insert a=rtcp-mux after the m=application line.
        int firstNl = appSection.IndexOf('\n');
        if (firstNl < 0) return sdp;
        string lineEnd = (firstNl > 0 && appSection[firstNl - 1] == '\r') ? "\r\n" : "\n";
        string patched = appSection.Substring(0, firstNl + 1) + "a=rtcp-mux" + lineEnd + appSection.Substring(firstNl + 1);
        Console.WriteLine("[WebRTC] Injected a=rtcp-mux into application m-section");
        return sdp.Substring(0, appIdx) + patched + sdp.Substring(endIdx);
    }

    public Task HandleAnswerAsync(string sdp)
    {
        Console.WriteLine($"[WebRTC] === ANSWER SDP ===\n{sdp}\n=== END ANSWER ===");
        var answer = new RTCSessionDescriptionInit
        {
            type = RTCSdpType.answer,
            sdp = sdp
        };
        var result = _peerConnection!.setRemoteDescription(answer);
        if (result != SetDescriptionResultEnum.OK)
        {
            Console.WriteLine($"[WebRTC] setRemoteDescription failed: {result}");
        }
        return Task.CompletedTask;
    }

    public void HandleIceCandidate(string candidateJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(candidateJson);
        var root = doc.RootElement;
        var candidate = new RTCIceCandidateInit
        {
            candidate = root.TryGetProperty("sdp", out var s) ? s.GetString() :
                        (root.TryGetProperty("candidate", out var c) ? c.GetString() : null),
            sdpMid = root.TryGetProperty("sdpMid", out var m) ? m.GetString() : null,
            sdpMLineIndex = root.TryGetProperty("sdpMLineIndex", out var i) ? (ushort)i.GetUInt16() : (ushort)0
        };
        if (!string.IsNullOrEmpty(candidate.candidate))
            _peerConnection?.addIceCandidate(candidate);
    }

    private async Task StartCaptureLoopAsync()
    {
        _captureCts = new CancellationTokenSource();
        _screenCapture.Initialize();

        int width = _screenCapture.Width;
        int height = _screenCapture.Height;

        AppLog.For("Capture").Information("loop_started {W}x{H} target={Fps}fps", width, height, TargetFps);
        timeBeginPeriod(1);

        const double frameIntervalMs = 1000.0 / TargetFps;
        const double stutterThresholdMs = 33.0;

        // Capacity=2: capture thread is always at most one frame ahead of encode thread.
        // On overflow the oldest frame is discarded so encode never falls behind.
        var frameQueue = new BlockingCollection<byte[]>(boundedCapacity: 2);

        var captureThread = new Thread(() =>
        {
            byte[]? lastFrame = null;
            while (!_captureCts.Token.IsCancellationRequested)
            {
                var frame = _screenCapture.CaptureFrame(16);
                if (frame == null)
                {
                    if (lastFrame != null) frame = lastFrame;
                    else continue;
                }
                else
                {
                    lastFrame = frame;
                }
                if (!frameQueue.TryAdd(frame))
                {
                    if (frameQueue.TryTake(out _))
                        frameQueue.TryAdd(frame);
                }
            }
            frameQueue.CompleteAdding();
        }) { IsBackground = true, Name = "CaptureThread" };
        captureThread.Start();

        int frameCount = 0;
        int reusedCount = 0;
        int dropCount = 0;
        int stutterCount = 0;
        double maxEncodeMs = 0;
        double sumEncodeMs = 0;
        int encodeSamples = 0;
        var reportSw = System.Diagnostics.Stopwatch.StartNew();
        var frameSw = System.Diagnostics.Stopwatch.StartNew();
        int lastReportFrames = 0;
        byte[]? lastEncodedFrame = null;

        while (!_captureCts.Token.IsCancellationRequested)
        {
            var loopStart = frameSw.ElapsedMilliseconds;

            bool reused = false;
            if (!frameQueue.TryTake(out byte[]? frame))
            {
                frame = lastEncodedFrame;
                reused = true;
                dropCount++;
            }

            if (_videoSource != null && frame != null)
            {
                if (!reused)
                {
                    lastEncodedFrame = frame;
                    try
                    {
                        FrameWatermarkRenderer.Apply(
                            frame, width, height,
                            _measuredFps, Interlocked.Read(ref _latestLatencyMs),
                            _measuredMbps, _encoderLabel);

                        var encSw = System.Diagnostics.Stopwatch.StartNew();
                        _videoSource.ExternalVideoSourceRawSample(
                            (uint)frameIntervalMs, width, height, frame,
                            VideoPixelFormatsEnum.Bgra);
                        encSw.Stop();
                        var encMs = encSw.Elapsed.TotalMilliseconds;
                        sumEncodeMs += encMs;
                        encodeSamples++;
                        if (encMs > maxEncodeMs) maxEncodeMs = encMs;
                        if (encMs > stutterThresholdMs)
                        {
                            stutterCount++;
                            AppLog.Stutter("encode", encMs, stutterThresholdMs);
                        }
                        frameCount++;
                    }
                    catch (Exception ex)
                    {
                        AppLog.For("Encode").Error(ex, "encode_error frame={Frame}", frameCount);
                    }
                }
                else
                {
                    reusedCount++;
                }
            }

            if (reportSw.ElapsedMilliseconds >= 2000)
            {
                int delta = frameCount - lastReportFrames;
                double winSec = reportSw.ElapsedMilliseconds / 1000.0;
                double winFps = delta / winSec;
                _measuredFps = winFps;
                long bytes = Interlocked.Exchange(ref _encodedBytesWindow, 0);
                _measuredMbps = bytes * 8.0 / winSec / 1_000_000.0;
                var avgEnc = encodeSamples > 0 ? sumEncodeMs / encodeSamples : 0;
                int measuredKbps = (int)(_measuredMbps * 1000);
                AppLog.Encode(delta, avgEnc, maxEncodeMs, dropCount, measuredKbps);
                AppLog.For("Perf").Information(
                    "FRAME_STATS fps={Fps:F1} encoded={Encoded} reused={Reused} drop={Drop} " +
                    "avgEncMs={EncMs:F1} maxEncMs={MaxMs:F1} mbps={Mbps:F1}",
                    winFps, delta, reusedCount, dropCount, avgEnc, maxEncodeMs, _measuredMbps);
                if (stutterCount > 0)
                    AppLog.For("Perf").Warning("STUTTER_WINDOW count={Count} window=2s", stutterCount);
                lastReportFrames = frameCount;
                reportSw.Restart();
                sumEncodeMs = 0; encodeSamples = 0; maxEncodeMs = 0; stutterCount = 0;
            }

            var sleepMs = frameIntervalMs - (frameSw.ElapsedMilliseconds - loopStart);
            if (sleepMs > 1.5)
            {
                try { await Task.Delay((int)(sleepMs - 1), _captureCts.Token); }
                catch (OperationCanceledException) { break; }
            }
            while (frameSw.ElapsedMilliseconds - loopStart < frameIntervalMs)
            {
                if (_captureCts.Token.IsCancellationRequested) break;
                Thread.SpinWait(50);
            }
        }

        timeEndPeriod(1);
        captureThread.Join(500);
    }

    public void Stop()
    {
        _captureCts?.Cancel();
        try { _audioCapture.Stop(); } catch (Exception ex) { AppLog.For("WebRTC").Warning(ex, "audioCapture.Stop failed"); }
        var pc = _peerConnection;
        _peerConnection = null;
        if (pc != null)
        {
            // SIPSorcery pc.close() may block on ICE/DTLS teardown — never run on caller (UI) thread.
            Task.Run(() =>
            {
                try { pc.close(); }
                catch (Exception ex) { AppLog.For("WebRTC").Warning(ex, "peerConnection.close failed"); }
            });
        }
        try { _h264Source?.Dispose(); } catch (Exception ex) { AppLog.For("WebRTC").Warning(ex, "h264Source.Dispose failed"); }
        _h264Source = null;
        _vp8EndPoint = null;
    }

    private void SubscribeRtcpRtt(RTCPeerConnection pc)
    {
        pc.OnReceiveReport += (ep, media, report) =>
        {
            try
            {
                foreach (var block in report.ReceiverReport?.ReceptionReports ?? new System.Collections.Generic.List<SIPSorcery.Net.ReceptionReportSample>())
                {
                    uint dlsr = block.DelaySinceLastSenderReport;
                    uint lsr  = block.LastSenderReportTimestamp;
                    if (lsr == 0 || dlsr == 0) continue;

                    uint nowNtp = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 65536L / 1000);
                    long rttNtp = (long)nowNtp - lsr - dlsr;
                    if (rttNtp > 0)
                    {
                        long rttMs = rttNtp * 1000 / 65536;
                        Interlocked.Exchange(ref _latestLatencyMs, rttMs);
                    }
                }
            }
            catch { }
        };
    }

    private IVideoSource CreateVideoSource(EncoderSettings settings)
    {
        if (settings.HwAccel)
        {
            try
            {
                EnsureFFmpegInitialised();
                var choice = HardwareEncoderDetector.Detect(
                    settings.HwAccel, settings.Vendor,
                    targetBitrateKbps: 30000, targetFps: TargetFps);
                _h264Source = new H264VideoSource(choice.EncoderName, choice.CodecOptions, TargetFps);
                _encoderLabel = choice.EncoderName;
                AppLog.For("WebRTC").Information("VideoSource H264 encoder={Encoder} hardware={IsHw}", choice.EncoderName, choice.IsHardware);
                return _h264Source;
            }
            catch (Exception ex)
            {
                AppLog.For("WebRTC").Warning(ex, "H264 source init failed → VP8 fallback");
            }
        }

        _vp8EndPoint = new VideoEncoderEndPoint();
        _encoderLabel = "VP8";
        AppLog.For("WebRTC").Information("VideoSource VP8 fallback");
        return _vp8EndPoint;
    }

    private static void EnsureFFmpegInitialised()
    {
        lock (_ffmpegInitLock)
        {
            if (_ffmpegInitialised) return;

            var libDir = AppContext.BaseDirectory;
            ffmpeg.RootPath = libDir;
            _ffmpegInitialised = true;
            AppLog.For("WebRTC").Information("FFmpeg initialised libDir={LibDir}", libDir);
        }
    }
}

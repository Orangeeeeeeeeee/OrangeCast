using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CaptureDemo;

/// <summary>
/// WASAPI Loopback 系统音频采集器
/// 采集系统渲染端点（扬声器/耳机）的输出音频流
/// 权限：普通用户，无需管理员
/// 支持：Windows Vista+
/// </summary>
public class WasapiCapture : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly List<byte[]> _capturedBuffers = new();
    private bool _disposed = false;

    public int SampleRate { get; private set; }
    public int Channels { get; private set; }
    public int BitsPerSample { get; private set; }

    public void Initialize()
    {
        _capture = new WasapiLoopbackCapture();
        var format = _capture.WaveFormat;

        SampleRate = format.SampleRate;       // 通常 48000 Hz
        Channels = format.Channels;           // 通常 2（立体声）
        BitsPerSample = format.BitsPerSample; // 通常 32（float）或 16

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        Console.WriteLine($"[WASAPI] 初始化成功: {SampleRate}Hz / {BitsPerSample}bit / {Channels}ch");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0)
        {
            // 系统静音时无数据，需填充静音帧保持 RTP 时间戳连续
            var silenceFrame = new byte[SampleRate / 50 * Channels * (BitsPerSample / 8)]; // 20ms 静音
            _capturedBuffers.Add(silenceFrame);
            return;
        }

        var buffer = new byte[e.BytesRecorded];
        Array.Copy(e.Buffer, buffer, e.BytesRecorded);
        _capturedBuffers.Add(buffer);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            Console.WriteLine($"[WASAPI] 采集停止异常: {e.Exception.Message}");
        else
            Console.WriteLine("[WASAPI] 采集正常停止");
    }

    public void StartCapture()
    {
        _capture?.StartRecording();
        Console.WriteLine("[WASAPI] 开始采集");
    }

    public void StopCapture()
    {
        _capture?.StopRecording();
        Console.WriteLine("[WASAPI] 停止采集");
    }

    public IReadOnlyList<byte[]> GetCapturedBuffers() => _capturedBuffers.AsReadOnly();

    public void Dispose()
    {
        if (!_disposed)
        {
            _capture?.Dispose();
            _disposed = true;
        }
    }
}

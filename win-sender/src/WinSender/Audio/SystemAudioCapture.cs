using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WinSender.Audio;

public class SystemAudioCapture : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private bool _disposed = false;

    public int SampleRate { get; private set; }
    public int Channels { get; private set; }
    public int BitsPerSample { get; private set; }

    public event EventHandler<byte[]>? AudioDataAvailable;

    public void Initialize()
    {
        _capture = new WasapiLoopbackCapture();
        SampleRate = _capture.WaveFormat.SampleRate;
        Channels = _capture.WaveFormat.Channels;
        BitsPerSample = _capture.WaveFormat.BitsPerSample;

        _capture.DataAvailable += (sender, e) =>
        {
            if (e.BytesRecorded == 0)
            {
                var silenceFrame = new byte[SampleRate / 50 * Channels * (BitsPerSample / 8)];
                AudioDataAvailable?.Invoke(this, silenceFrame);
                return;
            }
            var buffer = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, buffer, e.BytesRecorded);
            AudioDataAvailable?.Invoke(this, buffer);
        };

        Console.WriteLine($"[Audio] WASAPI loopback: {SampleRate}Hz/{BitsPerSample}bit/{Channels}ch");
    }

    public void Start() => _capture?.StartRecording();
    public void Stop() => _capture?.StopRecording();

    public void Dispose()
    {
        if (!_disposed)
        {
            _capture?.Dispose();
            _disposed = true;
        }
    }
}

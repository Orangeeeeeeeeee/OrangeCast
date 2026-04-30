using System;
using System.Threading;

namespace CaptureDemo;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Windows 采集技术验证 Demo ===");
        Console.WriteLine("注意：本 demo 需在 Windows 11 上运行");
        Console.WriteLine();

        DemoScreenCapture();
        DemoAudioCapture();

        Console.WriteLine("\n=== 验证完成 ===");
    }

    static void DemoScreenCapture()
    {
        Console.WriteLine("--- DXGI Desktop Duplication 验证 ---");
        using var capture = new DxgiCapture();
        capture.Initialize();

        var frame = capture.CaptureFrame(timeoutMs: 33);
        if (frame != null)
            Console.WriteLine($"[OK] 采集一帧成功，数据大小: {frame.Length} bytes");
        else
            Console.WriteLine("[WARN] 超时，无新帧（可能显示器无变化）");
    }

    static void DemoAudioCapture()
    {
        Console.WriteLine("\n--- WASAPI Loopback 验证 ---");
        using var capture = new WasapiCapture();
        capture.Initialize();

        capture.StartCapture();
        Console.WriteLine("采集 1 秒系统音频...");
        Thread.Sleep(1000);
        capture.StopCapture();

        var buffers = capture.GetCapturedBuffers();
        var totalBytes = 0;
        foreach (var buf in buffers) totalBytes += buf.Length;

        Console.WriteLine($"[OK] 采集完成，共 {buffers.Count} 个缓冲块，总计 {totalBytes} bytes");
        Console.WriteLine($"     格式: {capture.SampleRate}Hz / {capture.BitsPerSample}bit / {capture.Channels}ch");
    }
}

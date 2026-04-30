using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinSender.Abr;
using WinSender.Audio;
using WinSender.Capture;
using WinSender.Discovery;
using WinSender.Signaling;
using WinSender.UI;
using WinSender.WebRTC;

namespace WinSender;

class Program
{
    static async Task<int> Main(string[] args)
    {
        WinSender.Diagnostics.AppLog.Initialize();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WinSender.Diagnostics.AppLog.Shutdown();

        var logPath = Environment.GetEnvironmentVariable("WINSENDER_LOG");
        if (string.IsNullOrEmpty(logPath))
        {
            try
            {
                var logDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrangeCast");
                System.IO.Directory.CreateDirectory(logDir);
                logPath = System.IO.Path.Combine(logDir, "sender.log");
            }
            catch { logPath = null; }
        }
        if (!string.IsNullOrEmpty(logPath))
        {
            try
            {
                var logStream = new System.IO.StreamWriter(logPath, append: false) { AutoFlush = true };
                logStream.WriteLine($"=== win-sender started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={Environment.ProcessId} ===");
                Console.SetOut(new TeeWriter(Console.Out, logStream));
                Console.SetError(new TeeWriter(Console.Error, logStream));
            }
            catch { /* fall back to console only */ }
        }

        var rootCommand = new RootCommand("OrangeCast - Windows Sender");

        if (args.Length == 0)
        {
            using var mutex = new Mutex(initiallyOwned: true,
                name: "Global\\OrangeCast-{8F3A1C2D-47BE-4E90-B6D5-0F9A3C7E821B}",
                out bool createdNew);
            if (!createdNew)
            {
                ActivateExistingWindow();
                return 0;
            }
            RunMainWindow();
            return 0;
        }

        var versionOption = new Option<bool>("--ver", "Show version");
        var testCaptureOption = new Option<bool>("--test-capture", "Test screen capture");
        var durationOption = new Option<int>("--duration", () => 5, "Test duration in seconds");
        var discoverOption = new Option<bool>("--discover", "Discover TV devices on LAN");
        var connectOption = new Option<string?>("--connect", "Connect to TV by IP (e.g. 192.168.1.100:8765)");
        var codeOption = new Option<string?>("--code", "4-digit pairing code shown on TV");
        var simulateBwOption = new Option<int>("--simulate-bw", () => 0, "Simulate bandwidth limit in Kbps (0=disabled)");

        var trayOption = new Option<bool>("--tray", "Run as system tray application (GUI mode)");

        rootCommand.AddOption(versionOption);
        rootCommand.AddOption(testCaptureOption);
        rootCommand.AddOption(durationOption);
        rootCommand.AddOption(discoverOption);
        rootCommand.AddOption(connectOption);
        rootCommand.AddOption(codeOption);
        rootCommand.AddOption(simulateBwOption);
        rootCommand.AddOption(trayOption);

        rootCommand.SetHandler(async (version, testCapture, duration, discover, connect, code, simulateBw, tray) =>
        {
            if (version)
            {
                Console.WriteLine("win-sender 1.0.0");
                return;
            }

            if (tray)
            {
                RunTrayApp();
                return;
            }

            if (testCapture)
            {
                await RunCaptureTest(duration);
                return;
            }

            if (discover)
            {
                await RunDiscovery();
                return;
            }

            if (connect != null)
            {
                if (string.IsNullOrEmpty(code))
                {
                    Console.Write("Enter 4-digit pairing code from TV: ");
                    code = Console.ReadLine()?.Trim();
                }
                await RunSender(connect, code ?? "", simulateBw);
                return;
            }

            Console.WriteLine("Usage: win-sender [--ver] [--test-capture] [--discover] [--connect <ip:port> --code <4-digit>] [--tray]");
            Console.WriteLine("       win-sender --connect 192.168.1.100:8765 --code 1234");
            Console.WriteLine("       win-sender --connect 192.168.1.100:8765 --simulate-bw 3000");
            Console.WriteLine("       win-sender --tray   (system tray GUI mode)");

        }, versionOption, testCaptureOption, durationOption, discoverOption, connectOption, codeOption, simulateBwOption, trayOption);

        return await rootCommand.InvokeAsync(args);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    static void ActivateExistingWindow()
    {
        var current = System.Diagnostics.Process.GetCurrentProcess();
        var existing = System.Diagnostics.Process
            .GetProcessesByName(current.ProcessName)
            .FirstOrDefault(p => p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero);
        if (existing is null) return;
        ShowWindow(existing.MainWindowHandle, 9); // SW_RESTORE
        SetForegroundWindow(existing.MainWindowHandle);
    }

    static void RunMainWindow()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var mainForm = new MainWindow();
        mainForm.HandleCreated += (_, _) => WinSender.UI.WinFormsSync.CaptureUiContext();
        Application.Run(mainForm);
    }

    static void RunTrayApp()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var tray = new TrayApp();
        WebRtcSender? activeSender = null;
        SignalingClient? activeClient = null;

        tray.ConnectRequested += async (target, code) =>
        {
            tray.SetState(TrayState.Connecting);
            try
            {
                var client = new SignalingClient(target);
                await client.ConnectAsync();
                await client.SendAsync(new SignalingMessage("CONNECT_REQUEST", code));

                var pairingTcs = new TaskCompletionSource<bool>();
                client.MessageReceived += (msg) =>
                {
                    if (msg.Type == "CONNECT_ACCEPT") pairingTcs.TrySetResult(true);
                    else if (msg.Type == "CONNECT_REJECT") pairingTcs.TrySetResult(false);
                };

                var timeout = Task.Delay(30000);
                var done = await Task.WhenAny(pairingTcs.Task, timeout);
                if (done == timeout || !pairingTcs.Task.Result)
                {
                    tray.SetState(TrayState.Error, "配对失败");
                    tray.ShowBalloon("连接失败", done == timeout ? "配对超时（30秒）" : "连接码错误", ToolTipIcon.Error);
                    await client.DisconnectAsync();
                    return;
                }

                var screenCapture = new ScreenCapture();
                var audioCapture = new SystemAudioCapture();
                var abrController = new AbrController();
                var sender = new WebRtcSender(client, screenCapture, audioCapture, abrController);
                await sender.StartAsync(code);

                activeSender = sender;
                activeClient = client;
                tray.SetState(TrayState.Casting, target);
                tray.ShowBalloon("投屏已开始", $"正在投屏到 {target}");
            }
            catch (Exception ex)
            {
                tray.SetState(TrayState.Error, ex.Message);
                tray.ShowBalloon("连接失败", ex.Message, ToolTipIcon.Error);
            }
        };

        tray.DisconnectRequested += () =>
        {
            activeSender?.Stop();
            _ = activeClient?.DisconnectAsync();
            activeSender = null;
            activeClient = null;
            tray.SetState(TrayState.Idle);
            tray.ShowBalloon("已断开", "投屏已停止");
        };

        Application.Run();
    }

    static async Task RunCaptureTest(int durationSeconds)
    {
        Console.WriteLine($"Testing screen capture for {durationSeconds} seconds...");
        using var capture = new ScreenCapture();
        capture.Initialize();

        int frameCount = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (!cts.Token.IsCancellationRequested)
        {
            var frame = capture.CaptureFrame();
            if (frame != null) frameCount++;
            await Task.Delay(33, cts.Token).ContinueWith(_ => { });
        }

        sw.Stop();
        double avgFps = frameCount / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"avg_fps: {avgFps:F1}  total_frames: {frameCount}  duration: {sw.Elapsed.TotalSeconds:F1}s");
    }

    static async Task RunDiscovery()
    {
        Console.WriteLine("Discovering OrangeCast devices on LAN...");
        var discoverer = new MdnsDiscoverer();
        var devices = await discoverer.DiscoverAsync(TimeSpan.FromSeconds(5));

        if (devices.Count == 0)
        {
            Console.WriteLine("No devices found. Try --connect <ip:port> for manual connection.");
            return;
        }

        Console.WriteLine($"Found {devices.Count} device(s):");
        for (int i = 0; i < devices.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {devices[i].Name}  {devices[i].Host}:{devices[i].Port}");
        }
    }

    static async Task RunSender(string target, string pairingCode, int simulateBwKbps)
    {
        Console.WriteLine($"Connecting to {target}...");
        if (simulateBwKbps > 0)
            Console.WriteLine($"Bandwidth simulation: {simulateBwKbps} Kbps");

        var signalingClient = new SignalingClient(target);
        await signalingClient.ConnectAsync();

        Console.WriteLine("Sending pairing code...");
        await signalingClient.SendAsync(new SignalingMessage("CONNECT_REQUEST", pairingCode));

        var pairingTcs = new TaskCompletionSource<bool>();
        signalingClient.MessageReceived += (msg) =>
        {
            if (msg.Type == "CONNECT_ACCEPT") pairingTcs.TrySetResult(true);
            else if (msg.Type == "CONNECT_REJECT") pairingTcs.TrySetResult(false);
        };

        var pairingTimeout = Task.Delay(TimeSpan.FromSeconds(30));
        var completed = await Task.WhenAny(pairingTcs.Task, pairingTimeout);
        if (completed == pairingTimeout || !pairingTcs.Task.Result)
        {
            Console.WriteLine(completed == pairingTimeout ? "Pairing timeout (30s). Check code and try again." : "Pairing rejected. Wrong code.");
            await signalingClient.DisconnectAsync();
            return;
        }

        Console.WriteLine("Paired! Starting screen cast...");

        using var screenCapture = new ScreenCapture();
        using var audioCapture = new SystemAudioCapture();
        var abrController = new AbrController();
        if (simulateBwKbps > 0) abrController.SetSimulateBandwidth(simulateBwKbps);

        var webRtcSender = new WebRtcSender(signalingClient, screenCapture, audioCapture, abrController);
        await webRtcSender.StartAsync(pairingCode);

        Console.WriteLine("Casting... Press Ctrl+C to stop.");
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });

        webRtcSender.Stop();
        await signalingClient.DisconnectAsync();
        Console.WriteLine("Disconnected.");
    }
}

internal sealed class TeeWriter : System.IO.TextWriter
{
    private readonly System.IO.TextWriter _a;
    private readonly System.IO.TextWriter _b;
    public TeeWriter(System.IO.TextWriter a, System.IO.TextWriter b) { _a = a; _b = b; }
    public override System.Text.Encoding Encoding => _a.Encoding;
    public override void Write(char value) { try { _a.Write(value); } catch { } _b.Write(value); }
    public override void Write(string? value) { try { _a.Write(value); } catch { } _b.Write(value); }
    public override void WriteLine(string? value) { try { _a.WriteLine(value); } catch { } _b.WriteLine(value); }
    public override void Flush() { try { _a.Flush(); } catch { } _b.Flush(); }
}

using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace WinSender.Diagnostics;

public static class AppLog
{
    public static string LogDirectory { get; private set; } = "";

    public static void Initialize()
    {
        LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrangeCast", "logs");
        Directory.CreateDirectory(LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithProperty("App", "OrangeCast")
            .WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: Path.Combine(LogDirectory, "orangecast-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 50 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Category}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== OrangeCast started v{Version} pid={Pid} logDir={LogDir} ===",
            typeof(AppLog).Assembly.GetName().Version, Environment.ProcessId, LogDirectory);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating={IsTerminating})", e.IsTerminating);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    public static void Shutdown() => Log.CloseAndFlush();

    public static ILogger For(string category) => Log.ForContext("Category", category);

    public static void Connection(string evt, string remote, string? reason = null) =>
        For("Conn").Information("{Event} remote={Remote} reason={Reason}", evt, remote, reason ?? "-");

    public static void Disconnect(string remote, string reason) =>
        For("Conn").Warning("DISCONNECT remote={Remote} reason={Reason}", remote, reason);

    public static void FrameDrop(string source, int dropped, int total, string reason) =>
        For("Frame").Warning("DROP source={Source} dropped={Dropped} total={Total} reason={Reason}",
            source, dropped, total, reason);

    public static void Stutter(string stage, double elapsedMs, double thresholdMs) =>
        For("Perf").Warning("STUTTER stage={Stage} elapsed={Elapsed:F1}ms threshold={Threshold:F0}ms",
            stage, elapsedMs, thresholdMs);

    public static void Encode(int frames, double avgMs, double maxMs, int dropped, int kbps) =>
        For("Encode").Information("frames={Frames} avgMs={Avg:F1} maxMs={Max:F1} dropped={Drop} kbps={Kbps}",
            frames, avgMs, maxMs, dropped, kbps);

    public static void Abr(string change, int width, int height, int fps, int kbps) =>
        For("ABR").Information("{Change} {W}x{H}@{Fps} {Kbps}kbps",
            change, width, height, fps, kbps);

    public static void Signaling(string evt, string detail) =>
        For("Sig").Information("{Event} {Detail}", evt, detail);

    public static void IceState(string state) =>
        For("ICE").Information("state={State}", state);

    public static void PcState(string state) =>
        For("PC").Information("state={State}", state);
}

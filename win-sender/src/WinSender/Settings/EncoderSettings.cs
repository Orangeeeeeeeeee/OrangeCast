using System;
using System.IO;
using System.Text.Json;

namespace WinSender.Settings;

/// <summary>
/// Hardware encoder preferences. Persisted to %APPDATA%\AtvCast\settings.json.
/// </summary>
public sealed class EncoderSettings
{
    /// <summary>Enable GPU hardware acceleration. True → H264 via FFmpeg; False → software VP8 fallback.</summary>
    public bool HwAccel { get; set; } = true;

    /// <summary>Preferred vendor: "auto" | "nvidia" | "intel" | "amd". Used only when HwAccel = true.</summary>
    public string Vendor { get; set; } = "auto";

    public bool StartHotspot { get; set; } = false;

    public bool ShowCursor { get; set; } = true;

    private static string SettingsPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "OrangeCast");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static EncoderSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<EncoderSettings>(json);
                if (s != null)
                {
                    s.Vendor = NormalizeVendor(s.Vendor);
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EncoderSettings] Load failed: {ex.Message}");
        }
        return new EncoderSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EncoderSettings] Save failed: {ex.Message}");
        }
    }

    private static readonly string[] AllowedVendors = { "auto", "nvidia", "intel", "amd" };

    private static string NormalizeVendor(string? raw)
    {
        var v = (raw ?? "auto").ToLowerInvariant();
        return Array.IndexOf(AllowedVendors, v) >= 0 ? v : "auto";
    }
}

using System;
using System.Collections.Generic;
using System.Management;

namespace WinSender.WebRTC;

public static class GpuEnumerator
{
    public sealed record GpuInfo(string Vendor, string Description);

    public static List<GpuInfo> EnumerateGpus()
    {
        var result = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Description, PNPDeviceID FROM Win32_VideoController");
            foreach (var mo in searcher.Get())
            {
                string description = mo["Description"]?.ToString()?.Trim() ?? "Unknown";
                string pnp = mo["PNPDeviceID"]?.ToString() ?? "";
                string vendor = ParseVendorFromPnp(pnp);
                if (vendor == "unknown") continue;
                result.Add(new GpuInfo(vendor, description));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GpuEnumerator] WMI query failed: {ex.Message}");
        }
        return result;
    }

    private static string ParseVendorFromPnp(string pnp)
    {
        if (string.IsNullOrEmpty(pnp)) return "unknown";
        int i = pnp.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase);
        if (i < 0 || i + 8 > pnp.Length) return "unknown";
        string hex = pnp.Substring(i + 4, 4).ToUpperInvariant();
        return hex switch
        {
            "10DE" => "nvidia",
            "8086" => "intel",
            "1002" => "amd",   // ATI/Radeon discrete + most integrated
            "1022" => "amd",   // AMD APU integrated (rarely as GPU device but possible)
            _ => "unknown",
        };
    }
}

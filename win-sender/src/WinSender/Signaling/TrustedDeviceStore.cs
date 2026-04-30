using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinSender.Signaling;

public record TrustedDevice(
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("deviceName")] string DeviceName,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("lastHost")] string LastHost,
    [property: JsonPropertyName("lastPort")] int LastPort,
    [property: JsonPropertyName("lastSeenUtc")] DateTime LastSeenUtc);

public record LocalIdentity(
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("deviceName")] string DeviceName);

public class TrustedDeviceStore
{
    private readonly string _dir;
    private readonly string _devicesPath;
    private readonly string _identityPath;
    private readonly object _lock = new();

    private List<TrustedDevice> _devices = new();
    private LocalIdentity? _identity;

    public TrustedDeviceStore()
    {
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtvCast");
        Directory.CreateDirectory(_dir);
        _devicesPath = Path.Combine(_dir, "trusted_devices.json");
        _identityPath = Path.Combine(_dir, "identity.json");
        Load();
    }

    public LocalIdentity Identity
    {
        get
        {
            lock (_lock)
            {
                if (_identity == null)
                {
                    _identity = new LocalIdentity(
                        Guid.NewGuid().ToString("N").Substring(0, 16),
                        Environment.MachineName);
                    SaveIdentity();
                }
                return _identity;
            }
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_identityPath))
                _identity = JsonSerializer.Deserialize<LocalIdentity>(File.ReadAllText(_identityPath));
        }
        catch (Exception ex) { Console.WriteLine($"[TrustedStore] Load identity failed: {ex.Message}"); }

        try
        {
            if (File.Exists(_devicesPath))
                _devices = JsonSerializer.Deserialize<List<TrustedDevice>>(File.ReadAllText(_devicesPath)) ?? new();
        }
        catch (Exception ex) { Console.WriteLine($"[TrustedStore] Load devices failed: {ex.Message}"); _devices = new(); }
    }

    private void SaveIdentity()
    {
        try { File.WriteAllText(_identityPath, JsonSerializer.Serialize(_identity, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Console.WriteLine($"[TrustedStore] Save identity failed: {ex.Message}"); }
    }

    private void SaveDevices()
    {
        try { File.WriteAllText(_devicesPath, JsonSerializer.Serialize(_devices, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Console.WriteLine($"[TrustedStore] Save devices failed: {ex.Message}"); }
    }

    public TrustedDevice? Find(string deviceId)
    {
        lock (_lock) return _devices.FirstOrDefault(d => d.DeviceId == deviceId);
    }

    public TrustedDevice? FindByHost(string host, int port)
    {
        lock (_lock) return _devices.FirstOrDefault(d => d.LastHost == host && d.LastPort == port);
    }

    public void Upsert(TrustedDevice dev)
    {
        lock (_lock)
        {
            _devices.RemoveAll(d => d.DeviceId == dev.DeviceId);
            _devices.Add(dev);
            SaveDevices();
        }
    }

    public void Remove(string deviceId)
    {
        lock (_lock)
        {
            if (_devices.RemoveAll(d => d.DeviceId == deviceId) > 0) SaveDevices();
        }
    }

    public IReadOnlyList<TrustedDevice> All()
    {
        lock (_lock) return _devices.ToList();
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Makaretu.Dns;
using DnsMessage = Makaretu.Dns.Message;

namespace WinSender.Discovery;

public record TvDevice(string Name, string Host, int Port, string? DeviceId = null);

public class MdnsDiscoverer
{
    private const string ServiceType = "_atvcast._tcp";

    public async Task<List<TvDevice>> DiscoverAsync(TimeSpan timeout)
    {
        var found = new ConcurrentDictionary<string, TvDevice>();
        var pendingHosts = new ConcurrentDictionary<string, (string instance, int port, string? deviceId, string? friendlyName)>();

        try
        {
            using var mdns = new MulticastService();
            using var sd = new ServiceDiscovery(mdns);

            void TryHarvest(DnsMessage msg)
            {
                var allRecords = msg.Answers.Concat(msg.AdditionalRecords).ToList();

                foreach (var srv in allRecords.OfType<SRVRecord>())
                {
                    var instanceName = srv.Name.ToString();
                    if (!instanceName.Contains("_atvcast")) continue;

                    int port = srv.Port;
                    var hostname = srv.Target.ToString();

                    string? deviceId = null;
                    string? friendlyName = null;
                    var txt = allRecords.OfType<TXTRecord>().FirstOrDefault(t => t.Name.ToString() == instanceName);
                    if (txt != null)
                    {
                        foreach (var s in txt.Strings)
                        {
                            var idx = s.IndexOf('=');
                            if (idx <= 0) continue;
                            var k = s.Substring(0, idx);
                            var v = s.Substring(idx + 1);
                            if (k == "deviceId") deviceId = v;
                            else if (k == "name") friendlyName = v;
                        }
                    }

                    var aRec = allRecords.OfType<ARecord>().FirstOrDefault(a => a.Name.ToString() == hostname);
                    if (aRec != null)
                    {
                        var host = aRec.Address.ToString();
                        var name = friendlyName ?? instanceName.Split('.').First();
                        var key = $"{host}:{port}";
                        if (found.TryAdd(key, new TvDevice(name, host, port, deviceId)))
                            Console.WriteLine($"[mDNS] Resolved: {name} {host}:{port} id={deviceId ?? "-"}");
                    }
                    else
                    {
                        pendingHosts[hostname] = (instanceName, port, deviceId, friendlyName);
                        try { mdns.SendQuery(hostname, type: DnsType.A); } catch { }
                    }
                }

                foreach (var a in allRecords.OfType<ARecord>())
                {
                    var hostname = a.Name.ToString();
                    if (pendingHosts.TryRemove(hostname, out var p))
                    {
                        var host = a.Address.ToString();
                        var name = p.friendlyName ?? p.instance.Split('.').First();
                        var key = $"{host}:{p.port}";
                        if (found.TryAdd(key, new TvDevice(name, host, p.port, p.deviceId)))
                            Console.WriteLine($"[mDNS] Resolved (late A): {name} {host}:{p.port} id={p.deviceId ?? "-"}");
                    }
                }
            }

            sd.ServiceInstanceDiscovered += (sender, e) =>
            {
                Console.WriteLine($"[mDNS] Instance: {e.ServiceInstanceName}");
                TryHarvest(e.Message);
                try { mdns.SendQuery(e.ServiceInstanceName, type: DnsType.SRV); } catch { }
                try { mdns.SendQuery(e.ServiceInstanceName, type: DnsType.TXT); } catch { }
            };

            mdns.AnswerReceived += (sender, e) =>
            {
                try { TryHarvest(e.Message); }
                catch (Exception ex) { Console.WriteLine($"[mDNS] AnswerReceived err: {ex.Message}"); }
            };

            mdns.Start();
            sd.QueryServiceInstances(ServiceType);

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[mDNS] Discover failed: {ex.Message}");
        }

        return found.Values.ToList();
    }
}

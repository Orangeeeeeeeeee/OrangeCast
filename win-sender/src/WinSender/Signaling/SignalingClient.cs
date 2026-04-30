using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WinSender.Diagnostics;

namespace WinSender.Signaling;

public record SignalingMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] string? Payload = null);

public enum DisconnectReason { Normal, RemoteClosed, NetworkError, HeartbeatTimeout }

public class SignalingClient
{
    private readonly string _target;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private System.Threading.Timer? _heartbeatTimer;
    private DateTime _lastRecvUtc = DateTime.UtcNow;
    private int _disconnectedFired = 0;
    private int _heartbeatRunning = 0;

    public event Action<SignalingMessage>? MessageReceived;
    public event Action<DisconnectReason, string>? Disconnected;
    public event Action<Exception>? ErrorOccurred;

    public bool IsConnected => _ws?.State == WebSocketState.Open;
    public string Target => _target;
    public string RemoteEndpointDescription => _target;

    public SignalingClient(string target)
    {
        _target = target.StartsWith("ws://") ? target : $"ws://{target}";
    }

    public async Task ConnectAsync()
    {
        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        _disconnectedFired = 0;
        _lastRecvUtc = DateTime.UtcNow;
        await _ws.ConnectAsync(new Uri(_target), _cts.Token);
        Console.WriteLine($"[Signaling] Connected to {_target}");
        AppLog.Signaling("WS_CONNECTED", _target);
        _ = ReceiveLoopAsync();
        StartHeartbeat();
    }

    public async Task SendAsync(SignalingMessage msg)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var json = JsonSerializer.Serialize(msg);
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts!.Token);
            if (msg.Type != "HEARTBEAT")
                Console.WriteLine($"[Signaling] Sent: {msg.Type} ({bytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Signaling] Send FAILED for {msg.Type}: {ex.Message}");
            FireDisconnected(DisconnectReason.NetworkError, $"Send failed: {ex.Message}");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        try
        {
            if (_ws?.State == WebSocketState.Open)
            {
                try { await SendAsync(new SignalingMessage("DISCONNECT")); } catch { }
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
            }
        }
        finally
        {
            _cts?.Cancel();
            FireDisconnected(DisconnectReason.Normal, "Local disconnect");
        }
    }

    private void StartHeartbeat()
    {
        _heartbeatTimer = new System.Threading.Timer(_ =>
        {
            // Reentrancy guard: timer ticks may overlap if a previous SendAsync is still pending.
            if (Interlocked.Exchange(ref _heartbeatRunning, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_ws?.State != WebSocketState.Open) return;
                    await SendAsync(new SignalingMessage("HEARTBEAT"));
                    if ((DateTime.UtcNow - _lastRecvUtc).TotalSeconds > 12)
                    {
                        Console.WriteLine("[Signaling] Heartbeat timeout (>12s no recv)");
                        AppLog.Disconnect(_target, "heartbeat_timeout_12s");
                        FireDisconnected(DisconnectReason.HeartbeatTimeout, "No data for 12s");
                        try { _ws?.Abort(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.For("Sig").Warning(ex, "heartbeat tick failed");
                }
                finally
                {
                    Interlocked.Exchange(ref _heartbeatRunning, 0);
                }
            });
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[65536];

        while (_ws?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _ws.ReceiveAsync(buffer, _cts!.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    FireDisconnected(DisconnectReason.RemoteClosed, "WebSocket Close frame");
                    break;
                }
                _lastRecvUtc = DateTime.UtcNow;
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var msg = JsonSerializer.Deserialize<SignalingMessage>(json);
                if (msg != null)
                {
                    OnMessageReceived(msg);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Signaling] Receive error: {ex.Message}");
                ErrorOccurred?.Invoke(ex);
                FireDisconnected(DisconnectReason.NetworkError, ex.Message);
                break;
            }
        }
        // 循环退出兜底
        FireDisconnected(DisconnectReason.NetworkError, "Receive loop exited");
    }

    private void FireDisconnected(DisconnectReason reason, string detail)
    {
        if (Interlocked.Exchange(ref _disconnectedFired, 1) == 1) return;
        Console.WriteLine($"[Signaling] Disconnected: {reason} ({detail})");
        AppLog.Disconnect(_target, $"{reason}:{detail}");
        try { Disconnected?.Invoke(reason, detail); } catch { }
    }

    private void OnMessageReceived(SignalingMessage msg)
    {
        if (msg.Type != "HEARTBEAT")
            Console.WriteLine($"[Signaling] Received: {msg.Type}");
        MessageReceived?.Invoke(msg);
    }
}

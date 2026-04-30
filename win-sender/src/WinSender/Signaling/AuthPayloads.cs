using System.Text.Json.Serialization;

namespace WinSender.Signaling;

public record AuthInfo(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string Value);

public record ConnectRequestPayload(
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("deviceName")] string DeviceName,
    [property: JsonPropertyName("auth")] AuthInfo Auth);

public record ConnectAcceptPayload(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("deviceName")] string DeviceName);

public record ConnectRejectPayload(
    [property: JsonPropertyName("reason")] string Reason);

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SyncWallpaper.Core;

public static class ModuleIpcProtocol
{
    public const int CurrentVersion = 1;
    public const string Version = "1";
}

public sealed record ModuleIpcMessage(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("moduleInstanceId")] string ModuleInstanceId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] JsonElement? Payload = null);

public sealed record ModuleIpcResponse(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("moduleInstanceId")] string ModuleInstanceId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("errorCode")] string? ErrorCode = null,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null,
    [property: JsonPropertyName("payload")] JsonElement? Payload = null,
    [property: JsonPropertyName("timestampUtc")] DateTime? TimestampUtc = null);

public static class ModuleIpcJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(ModuleIpcMessage message) => JsonSerializer.Serialize(message, Options);
    public static string Serialize(ModuleIpcResponse response) => JsonSerializer.Serialize(response, Options);
    public static bool TryDeserialize(string line, out ModuleIpcMessage? message)
    {
        try
        {
            message = JsonSerializer.Deserialize<ModuleIpcMessage>(line, Options);
            return message is not null;
        }
        catch
        {
            message = null;
            return false;
        }
    }

    public static bool TryDeserializeResponse(string line, out ModuleIpcResponse? response)
    {
        try
        {
            response = JsonSerializer.Deserialize<ModuleIpcResponse>(line, Options);
            return response is not null;
        }
        catch
        {
            response = null;
            return false;
        }
    }
}

using System.Text.Json;

namespace QuanLyTro.Shared.Protocol;

public sealed record RequestPacket(string RequestId, string Action, string? Token, JsonElement Data)
{
    public static RequestPacket Create(string action, string? token, object? data = null)
    {
        var json = data is null ? "{}" : JsonDefaults.Serialize(data);
        return new RequestPacket(Guid.NewGuid().ToString("N"), action, token,
            JsonSerializer.Deserialize<JsonElement>(json));
    }
}

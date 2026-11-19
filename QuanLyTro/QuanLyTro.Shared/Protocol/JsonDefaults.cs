using System.Text.Json;
using System.Text.Json.Serialization;
using QuanLyTro.Shared.Models;

namespace QuanLyTro.Shared.Protocol;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) =>
        json is null ? default : JsonSerializer.Deserialize<T>(json, Options);
}

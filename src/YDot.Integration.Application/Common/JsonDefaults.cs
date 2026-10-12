using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YDot.Integration.Application.Common;

/// <summary>The JSON settings for everything the worker stores in jsonb columns and output files.</summary>
public static class JsonDefaults
{
    /// <summary>camelCase, enums as strings, nulls omitted, readable non-ASCII (₹, names).</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The same, indented - for the summary.json output file.</summary>
    public static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string? SerializeOrNull(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, value.GetType(), Options);
}

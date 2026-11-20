using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeField.Exporter.Core;

/// <summary>Shared JSON settings for all bundle files.</summary>
public static class BundleJson
{
    /// <summary>Compact (non-indented) options used for data files.</summary>
    public static JsonSerializerOptions Options { get; } = Create(false);

    /// <summary>Indented options used for manifest.json.</summary>
    public static JsonSerializerOptions IndentedOptions { get; } = Create(true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
            NumberHandling = JsonNumberHandling.Strict,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        o.Converters.Add(new ObjectValueConverter());
        return o;
    }

    public static string Serialize<T>(T value, bool indented = false)
        => JsonSerializer.Serialize(value, indented ? IndentedOptions : Options);

    public static byte[] SerializeToUtf8<T>(T value, bool indented = false)
        => JsonSerializer.SerializeToUtf8Bytes(value, indented ? IndentedOptions : Options);

    public static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("JSON deserialized to null.");

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8)
        => JsonSerializer.Deserialize<T>(utf8, Options) ?? throw new JsonException("JSON deserialized to null.");

    public static T Deserialize<T>(Stream utf8)
        => JsonSerializer.Deserialize<T>(utf8, Options) ?? throw new JsonException("JSON deserialized to null.");

    /// <summary>Maps JSON scalars to string / long / double / bool when reading <c>object</c> values.</summary>
    private sealed class ObjectValueConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
        {
            JsonTokenType.String => r.GetString(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => r.TryGetInt64(out var l) ? (object)l : r.GetDouble(),
            JsonTokenType.Null => null,
            _ => JsonDocument.ParseValue(ref r).RootElement.Clone(),
        };

        public override void Write(Utf8JsonWriter w, object value, JsonSerializerOptions o)
        {
            switch (value)
            {
                case string s: w.WriteStringValue(s); break;
                case bool b: w.WriteBooleanValue(b); break;
                case double d: if (double.IsFinite(d)) w.WriteNumberValue(d); else w.WriteNullValue(); break;
                case float f: if (float.IsFinite(f)) w.WriteNumberValue(f); else w.WriteNullValue(); break;
                case long l: w.WriteNumberValue(l); break;
                case int i: w.WriteNumberValue(i); break;
                case decimal m: w.WriteNumberValue(m); break;
                case JsonElement je: je.WriteTo(w); break;
                default: w.WriteStringValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)); break;
            }
        }
    }
}

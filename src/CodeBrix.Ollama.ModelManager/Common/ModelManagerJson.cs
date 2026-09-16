using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The one <see cref="JsonSerializerOptions"/> instance the library uses for every manifest, config,
/// parameters and messages blob, so that what it writes is byte-compatible with what Ollama reads.
/// </summary>
internal static class ModelManagerJson
{
    /// <summary>
    /// Shared serializer options: snake_case is NOT applied globally because Ollama's JSON mixes
    /// camelCase (manifest: schemaVersion, mediaType) and snake_case (config: model_format), so every
    /// serialized type names its properties explicitly with <see cref="JsonPropertyNameAttribute"/>.
    /// Nulls are omitted, matching Go's omitempty on the optional fields.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Serializes <paramref name="value"/> as compact JSON followed by a single newline, which is what
    /// Go's json.Encoder emits and therefore what Ollama writes for manifests and blobs.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The UTF-8 bytes of the JSON document plus a trailing newline.</returns>
    public static byte[] SerializeLikeGo<T>(T value)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        byte[] result = new byte[body.Length + 1];
        body.CopyTo(result, 0);
        result[body.Length] = (byte)'\n';
        return result;
    }

    /// <summary>
    /// Deserializes UTF-8 JSON into <typeparamref name="T"/> using the shared options.
    /// </summary>
    /// <typeparam name="T">The type to produce.</typeparam>
    /// <param name="utf8Json">The JSON document.</param>
    /// <returns>The deserialized value.</returns>
    public static T Deserialize<T>(byte[] utf8Json)
    {
        return JsonSerializer.Deserialize<T>(utf8Json, Options);
    }

    /// <summary>
    /// Deserializes JSON text into <typeparamref name="T"/> using the shared options.
    /// </summary>
    /// <typeparam name="T">The type to produce.</typeparam>
    /// <param name="json">The JSON document.</param>
    /// <returns>The deserialized value.</returns>
    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}

using System.Text.Json.Serialization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One entry of a <see cref="ModelManifest"/>: a blob identified by its sha256 digest, with the media
/// type that says what it holds. The JSON shape is exactly Ollama's.
/// </summary>
public sealed class ModelLayer
{
    /// <summary>
    /// Initializes an empty layer for deserialization.
    /// </summary>
    public ModelLayer()
    {
    }

    /// <summary>
    /// Initializes a layer.
    /// </summary>
    /// <param name="mediaType">One of the <see cref="MediaTypes"/> constants.</param>
    /// <param name="digest">The blob digest in <c>sha256:&lt;64 hex&gt;</c> form.</param>
    /// <param name="size">The blob size in bytes.</param>
    public ModelLayer(string mediaType, string digest, long size)
    {
        MediaType = mediaType;
        Digest = digest;
        Size = size;
    }

    /// <summary>One of the <see cref="MediaTypes"/> constants.</summary>
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; }

    /// <summary>The blob digest in <c>sha256:&lt;64 hex&gt;</c> form.</summary>
    [JsonPropertyName("digest")]
    public string Digest { get; set; }

    /// <summary>The blob size in bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// For layers created from another model or an imported file, the name of that source; otherwise
    /// <see langword="null"/>.
    /// </summary>
    [JsonPropertyName("from")]
    public string From { get; set; }

    /// <summary>
    /// For safetensors tensor layers, the tensor name; otherwise <see langword="null"/>.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

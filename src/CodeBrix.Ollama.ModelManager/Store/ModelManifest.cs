using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// A model manifest as stored under <c>manifests/&lt;host&gt;/&lt;namespace&gt;/&lt;model&gt;/&lt;tag&gt;</c>
/// and as served by the registry. The JSON shape is exactly Ollama's.
/// </summary>
public sealed class ModelManifest
{
    /// <summary>Always 2.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    /// <summary>Always <see cref="MediaTypes.Manifest"/>.</summary>
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = MediaTypes.Manifest;

    /// <summary>The config layer (<see cref="MediaTypes.Config"/>).</summary>
    [JsonPropertyName("config")]
    public ModelLayer Config { get; set; }

    /// <summary>The content layers in manifest order.</summary>
    [JsonPropertyName("layers")]
    public List<ModelLayer> Layers { get; set; } = new List<ModelLayer>();

    /// <summary>
    /// The total size in bytes of every layer plus the config layer.
    /// </summary>
    /// <returns>The sum of layer sizes.</returns>
    public long GetTotalSize()
    {
        long size = 0;
        if (Layers != null)
        {
            foreach (ModelLayer layer in Layers)
            {
                size += layer.Size;
            }
        }
        if (Config != null)
        {
            size += Config.Size;
        }
        return size;
    }
}

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The config layer of a model: what Ollama calls ConfigV2. Fields this library does not interpret are
/// preserved in <see cref="AdditionalProperties"/> so a config read from the registry is written back
/// unchanged when a derived model is created.
/// </summary>
public sealed class ModelConfig
{
    /// <summary>The weights format; "gguf" for every model this library can create.</summary>
    [JsonPropertyName("model_format")]
    public string ModelFormat { get; set; }

    /// <summary>The primary architecture family, for example "llama" or "qwen2".</summary>
    [JsonPropertyName("model_family")]
    public string ModelFamily { get; set; }

    /// <summary>Every architecture family present (a model with a projector lists both).</summary>
    [JsonPropertyName("model_families")]
    public List<string> ModelFamilies { get; set; }

    /// <summary>The parameter count as a human string, for example "134.52M" or "7.2B".</summary>
    [JsonPropertyName("model_type")]
    public string ModelType { get; set; }

    /// <summary>The quantization level, for example "Q8_0" or "Q4_K_M".</summary>
    [JsonPropertyName("file_type")]
    public string FileType { get; set; }

    /// <summary>Ollama's built-in renderer name, when the model names one; otherwise <see langword="null"/>.</summary>
    [JsonPropertyName("renderer")]
    public string Renderer { get; set; }

    /// <summary>Ollama's built-in parser name, when the model names one; otherwise <see langword="null"/>.</summary>
    [JsonPropertyName("parser")]
    public string Parser { get; set; }

    /// <summary>The minimum Ollama version the model declares it requires, when any; otherwise <see langword="null"/>.</summary>
    [JsonPropertyName("requires")]
    public string Requires { get; set; }

    /// <summary>Capabilities declared in the config itself (used by remote models); usually <see langword="null"/>.</summary>
    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; }

    /// <summary>The context length declared in the config, or 0 when it is not declared there.</summary>
    [JsonPropertyName("context_length")]
    public int ContextLength { get; set; }

    /// <summary>The embedding length declared in the config, or 0 when it is not declared there.</summary>
    [JsonPropertyName("embedding_length")]
    public int EmbeddingLength { get; set; }

    /// <summary>
    /// Every config property this library does not model, preserved verbatim for round-tripping.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties { get; set; }
}

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// A model's default parameters: the PARAMETER lines of its Modelfile, stored as the JSON object in its
/// params layer. Every known Ollama parameter has a typed property; a value that is not set is
/// <see langword="null"/> and is omitted from the JSON. Parameters this library does not know are kept
/// in <see cref="AdditionalParameters"/> so nothing is lost when a model is copied or derived.
/// </summary>
/// <remarks>
/// The property names and JSON names follow Ollama's api.Options exactly. Which parameters a runner
/// honours is the runner's business; the store only carries them.
/// </remarks>
public sealed class ModelParameters
{
    /// <summary>Number of tokens from the start of the context to keep when it overflows.</summary>
    [JsonPropertyName("num_keep")]
    public int? NumKeep { get; set; }

    /// <summary>Random seed for sampling.</summary>
    [JsonPropertyName("seed")]
    public int? Seed { get; set; }

    /// <summary>Maximum number of tokens to generate (-1 for unlimited).</summary>
    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; set; }

    /// <summary>Top-k sampling cutoff.</summary>
    [JsonPropertyName("top_k")]
    public int? TopK { get; set; }

    /// <summary>Top-p (nucleus) sampling cutoff.</summary>
    [JsonPropertyName("top_p")]
    public float? TopP { get; set; }

    /// <summary>Min-p sampling cutoff.</summary>
    [JsonPropertyName("min_p")]
    public float? MinP { get; set; }

    /// <summary>Typical-p sampling cutoff (deprecated upstream, still carried).</summary>
    [JsonPropertyName("typical_p")]
    public float? TypicalP { get; set; }

    /// <summary>How many recent tokens the repeat penalty looks back over.</summary>
    [JsonPropertyName("repeat_last_n")]
    public int? RepeatLastN { get; set; }

    /// <summary>Sampling temperature.</summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>Repeat penalty.</summary>
    [JsonPropertyName("repeat_penalty")]
    public float? RepeatPenalty { get; set; }

    /// <summary>Presence penalty.</summary>
    [JsonPropertyName("presence_penalty")]
    public float? PresencePenalty { get; set; }

    /// <summary>Frequency penalty.</summary>
    [JsonPropertyName("frequency_penalty")]
    public float? FrequencyPenalty { get; set; }

    /// <summary>Stop sequences; generation ends when any is produced.</summary>
    [JsonPropertyName("stop")]
    public List<string> Stop { get; set; }

    /// <summary>Context window size in tokens.</summary>
    [JsonPropertyName("num_ctx")]
    public int? NumCtx { get; set; }

    /// <summary>Prompt-processing batch size.</summary>
    [JsonPropertyName("num_batch")]
    public int? NumBatch { get; set; }

    /// <summary>Number of layers to offload to the GPU.</summary>
    [JsonPropertyName("num_gpu")]
    public int? NumGpu { get; set; }

    /// <summary>Which GPU holds the non-offloaded work.</summary>
    [JsonPropertyName("main_gpu")]
    public int? MainGpu { get; set; }

    /// <summary>Whether to memory-map the model file.</summary>
    [JsonPropertyName("use_mmap")]
    public bool? UseMmap { get; set; }

    /// <summary>Number of CPU threads.</summary>
    [JsonPropertyName("num_thread")]
    public int? NumThread { get; set; }

    /// <summary>Draft-model lookahead for speculative decoding.</summary>
    [JsonPropertyName("draft_num_predict")]
    public int? DraftNumPredict { get; set; }

    /// <summary>
    /// Every parameter this library does not model, preserved verbatim for round-tripping (this is
    /// where deprecated parameters such as mirostat land when an older manifest carries them).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalParameters { get; set; }
}

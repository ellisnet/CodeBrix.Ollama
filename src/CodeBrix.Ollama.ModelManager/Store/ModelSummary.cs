using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One row of <see cref="IModelStore.ListAsync"/>: what can be said about a model from its manifest and
/// config layer alone, without opening the weights.
/// </summary>
public sealed class ModelSummary
{
    /// <summary>The fully qualified name.</summary>
    public ModelName Name { get; set; }

    /// <summary>The name in its shortest display form, for example "smollm:135m" or "hf.co/user/repo:Q8_0".</summary>
    public string DisplayName { get; set; }

    /// <summary>The sha256 of the manifest file as lowercase hex without a prefix.</summary>
    public string Digest { get; set; }

    /// <summary>The total size in bytes of every layer including the config.</summary>
    public long Size { get; set; }

    /// <summary>When the manifest file was last written.</summary>
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>The config layer contents, or an empty config when the manifest has none.</summary>
    public ModelConfig Config { get; set; }
}

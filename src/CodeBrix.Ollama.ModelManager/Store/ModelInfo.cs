using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Everything <see cref="IModelStore.ShowAsync"/> can say about a model: the manifest, the config, every
/// text and JSON layer decoded, and the GGUF metadata of the model weights.
/// </summary>
public sealed class ModelInfo
{
    /// <summary>The fully qualified name.</summary>
    public ModelName Name { get; set; }

    /// <summary>The name in its shortest display form.</summary>
    public string DisplayName { get; set; }

    /// <summary>The sha256 of the manifest file as lowercase hex without a prefix.</summary>
    public string Digest { get; set; }

    /// <summary>The total size in bytes of every layer including the config.</summary>
    public long Size { get; set; }

    /// <summary>When the manifest file was last written.</summary>
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>The manifest as read from disk.</summary>
    public ModelManifest Manifest { get; set; }

    /// <summary>The config layer contents, or an empty config when the manifest has none.</summary>
    public ModelConfig Config { get; set; }

    /// <summary>The template layer text, or <see langword="null"/> when the model has none.</summary>
    public string Template { get; set; }

    /// <summary>The system layer text, or <see langword="null"/> when the model has none.</summary>
    public string System { get; set; }

    /// <summary>The params layer decoded, or <see langword="null"/> when the model has none.</summary>
    public ModelParameters Parameters { get; set; }

    /// <summary>Every license layer's text, in manifest order; empty when there are none.</summary>
    public IReadOnlyList<string> Licenses { get; set; }

    /// <summary>The messages layer decoded; empty when there is none.</summary>
    public IReadOnlyList<ModelMessage> Messages { get; set; }

    /// <summary>The metadata of the first model-weights GGUF, or <see langword="null"/> when the model has no GGUF weights.</summary>
    public GgufMetadata Metadata { get; set; }

    /// <summary>The metadata of each projector GGUF, in manifest order; empty when there are none.</summary>
    public IReadOnlyList<GgufMetadata> ProjectorMetadata { get; set; }

    /// <summary>The capabilities inferred for the model.</summary>
    public IReadOnlyList<ModelCapability> Capabilities { get; set; }

    /// <summary>
    /// The model reconstructed as Modelfile text, the way <c>ollama show --modelfile</c> prints it.
    /// </summary>
    public string ModelfileText { get; set; }
}

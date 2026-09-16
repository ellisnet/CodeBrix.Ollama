using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The result of <see cref="IModelStore.ResolveAsync"/>: every file on disk that an in-process runner
/// needs to load the model, plus the text and parameter layers that shape a conversation with it.
/// Nothing here is opened or read; it is paths and decoded small blobs only.
/// </summary>
public sealed class ResolvedModel
{
    /// <summary>The fully qualified name.</summary>
    public ModelName Name { get; set; }

    /// <summary>The absolute path of the manifest file.</summary>
    public string ManifestPath { get; set; }

    /// <summary>The absolute path of the model-weights GGUF blob (the first model layer).</summary>
    public string ModelPath { get; set; }

    /// <summary>The absolute paths of any further model-layer GGUF blobs of a split model, in order; empty otherwise.</summary>
    public IReadOnlyList<string> ModelShardPaths { get; set; }

    /// <summary>The absolute paths of projector GGUF blobs, in manifest order; empty when there are none.</summary>
    public IReadOnlyList<string> ProjectorPaths { get; set; }

    /// <summary>The absolute paths of adapter GGUF blobs, in manifest order; empty when there are none.</summary>
    public IReadOnlyList<string> AdapterPaths { get; set; }

    /// <summary>The absolute path of the draft-model GGUF blob, or <see langword="null"/> when there is none.</summary>
    public string DraftPath { get; set; }

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

    /// <summary>The config layer contents, or an empty config when the manifest has none.</summary>
    public ModelConfig Config { get; set; }
}

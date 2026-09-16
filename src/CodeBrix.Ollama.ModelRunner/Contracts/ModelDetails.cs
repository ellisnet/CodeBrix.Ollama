using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What the engine knows about a model file: from <see cref="ModelRunner.ProbeAsync"/> without loading the
/// weights, or from <see cref="IRunningModel.Details"/> for a loaded model.
/// </summary>
public sealed class ModelDetails
{
    /// <summary>The path of the model file.</summary>
    public string Path { get; init; }

    /// <summary>The size of the model file in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>The engine's one-line description of the model, for example the architecture, size class and quantization.</summary>
    public string Description { get; init; }

    /// <summary>The architecture name from the file, for example "llama" or "qwen35moe".</summary>
    public string Architecture { get; init; }

    /// <summary>The model's own name from the file, when it records one. May be <see langword="null"/>.</summary>
    public string Name { get; init; }

    /// <summary>The number of parameters.</summary>
    public ulong ParameterCount { get; init; }

    /// <summary>The number of bytes the weights occupy in memory when loaded.</summary>
    public ulong WeightsSize { get; init; }

    /// <summary>The context length the model was trained with.</summary>
    public int TrainingContextLength { get; init; }

    /// <summary>The embedding dimension.</summary>
    public int EmbeddingLength { get; init; }

    /// <summary>The number of transformer layers.</summary>
    public int LayerCount { get; init; }

    /// <summary>The number of attention heads.</summary>
    public int HeadCount { get; init; }

    /// <summary>The vocabulary size.</summary>
    public int VocabularySize { get; init; }

    /// <summary>Whether the model mixes attention and recurrent (state-space or delta-net) layers.</summary>
    public bool IsHybrid { get; init; }

    /// <summary>Whether the model is purely recurrent.</summary>
    public bool IsRecurrent { get; init; }

    /// <summary>Whether the model has an encoder.</summary>
    public bool HasEncoder { get; init; }

    /// <summary>Whether the model has a decoder.</summary>
    public bool HasDecoder { get; init; }

    /// <summary>The Jinja chat template embedded in the file, or <see langword="null"/> when there is none.</summary>
    public string ChatTemplate { get; init; }

    /// <summary>Every string-valued metadata key in the file and its value. Array values are omitted.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
}

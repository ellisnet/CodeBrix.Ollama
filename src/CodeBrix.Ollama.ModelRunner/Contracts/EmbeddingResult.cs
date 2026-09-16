using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The embeddings of one or more inputs.
/// </summary>
public sealed class EmbeddingResult
{
    /// <summary>One vector per input, in input order. Every vector has <see cref="Dimensions"/> elements.</summary>
    public IReadOnlyList<float[]> Embeddings { get; init; }

    /// <summary>The length of every vector.</summary>
    public int Dimensions { get; init; }

    /// <summary>The total number of tokens across all inputs.</summary>
    public int PromptTokens { get; init; }
}

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// How per-token embeddings are reduced to one vector per input.
/// </summary>
public enum EmbeddingPooling
{
    /// <summary>Whatever the model file specifies, falling back to <see cref="Mean"/>. The default.</summary>
    Unspecified = -1,

    /// <summary>No pooling; the vector of the last token is returned.</summary>
    None = 0,

    /// <summary>The arithmetic mean over all tokens.</summary>
    Mean = 1,

    /// <summary>The vector of the first (classification) token.</summary>
    Cls = 2,

    /// <summary>The vector of the last token.</summary>
    Last = 3,

    /// <summary>The model's reranking head, producing one score per input.</summary>
    Rank = 4,
}

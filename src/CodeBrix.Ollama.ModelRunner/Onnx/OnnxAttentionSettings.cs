namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a grouped-query attention node settled when the model was loaded: how many heads of each kind, what
/// the scores are scaled by, whether the positions are turned and how.
/// </summary>
internal sealed class OnnxAttentionSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="heads">How many attention heads the query has.</param>
    /// <param name="keyValueHeads">How many the key and the value have; the query's is a multiple of it.</param>
    /// <param name="scale">What the scores are multiplied by, or nought for one over the root of the head size.</param>
    /// <param name="causal">Whether a position may only see itself and the positions before it.</param>
    /// <param name="rotary">Whether the positions are turned by a rotary embedding.</param>
    /// <param name="interleaved">Whether a turned pair is two neighbours rather than two halves.</param>
    internal OnnxAttentionSettings(
        int heads, int keyValueHeads, float scale, bool causal, bool rotary, bool interleaved)
    {
        Heads = heads;
        KeyValueHeads = keyValueHeads;
        Scale = scale;
        Causal = causal;
        Rotary = rotary;
        Interleaved = interleaved;
    }

    /// <summary>How many attention heads the query has.</summary>
    internal int Heads { get; }

    /// <summary>How many heads the key and the value have; the query's count is a multiple of it.</summary>
    internal int KeyValueHeads { get; }

    /// <summary>What the scores are multiplied by, or nought for one over the root of the head size.</summary>
    internal float Scale { get; }

    /// <summary>Whether a position may only see itself and the positions before it.</summary>
    internal bool Causal { get; }

    /// <summary>Whether the positions are turned by a rotary embedding.</summary>
    internal bool Rotary { get; }

    /// <summary>Whether a turned pair is two neighbours rather than two halves.</summary>
    internal bool Interleaved { get; }

    /// <summary>How many query heads share one key-and-value head.</summary>
    internal int HeadsPerKeyValueHead => Heads / KeyValueHeads;
}

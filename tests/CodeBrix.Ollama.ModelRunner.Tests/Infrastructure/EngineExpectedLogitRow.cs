namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// One line of the conformance gate's EXPECTED.txt: the reference logits for one position of the fixed
/// twelve-token prompt.
/// </summary>
public sealed class EngineExpectedLogitRow
{
    /// <summary>The position in the prompt.</summary>
    public int Position { get; init; }

    /// <summary>The token id at that position.</summary>
    public int Token { get; init; }

    /// <summary>The index of the largest logit.</summary>
    public int Argmax { get; init; }

    /// <summary>The whole row of logits, one per vocabulary entry.</summary>
    public double[] Logits { get; init; }
}

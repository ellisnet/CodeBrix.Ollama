using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One token the sampler is still considering: its number, the score the model gave it, and the probability
/// that score works out to among whatever is left beside it.
/// </summary>
/// <remarks>
/// The probability is recomputed after every truncation, because each stage compares a token against what
/// SURVIVED the stage before it rather than against the whole vocabulary.
/// </remarks>
internal readonly struct CausalLmCandidate
{
    /// <summary>Orders candidates by score, strongest first, ties going to the lower token number.</summary>
    internal static readonly IComparer<CausalLmCandidate> Strongest = new StrongestFirst();

    /// <summary>Creates a candidate from a score.</summary>
    /// <param name="token">The token number.</param>
    /// <param name="logit">The score the model gave it.</param>
    internal CausalLmCandidate(int token, float logit)
    {
        Token = token;
        Logit = logit;
        Probability = 0.0;
    }

    private CausalLmCandidate(int token, float logit, double probability)
    {
        Token = token;
        Logit = logit;
        Probability = probability;
    }

    /// <summary>The token number.</summary>
    internal int Token { get; }

    /// <summary>The score the model gave it, after any penalties and any temperature.</summary>
    internal float Logit { get; }

    /// <summary>Its probability among the candidates still standing.</summary>
    internal double Probability { get; }

    /// <summary>The same candidate with another score.</summary>
    /// <param name="logit">The score.</param>
    /// <returns>The candidate.</returns>
    internal CausalLmCandidate WithLogit(float logit) => new CausalLmCandidate(Token, logit, Probability);

    /// <summary>The same candidate with another probability.</summary>
    /// <param name="probability">The probability.</param>
    /// <returns>The candidate.</returns>
    internal CausalLmCandidate WithProbability(double probability) =>
        new CausalLmCandidate(Token, Logit, probability);

    private sealed class StrongestFirst : IComparer<CausalLmCandidate>
    {
        public int Compare(CausalLmCandidate left, CausalLmCandidate right)
        {
            int byScore = right.Logit.CompareTo(left.Logit);
            return byScore != 0 ? byScore : left.Token.CompareTo(right.Token);
        }
    }
}

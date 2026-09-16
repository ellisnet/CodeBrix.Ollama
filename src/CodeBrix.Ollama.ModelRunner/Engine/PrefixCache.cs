using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The record of which tokens are currently in the context's key/value memory, and how much of a new prompt
/// that record lets the engine skip.
/// </summary>
/// <remarks>
/// <para>
/// Every request carries its whole prompt, so a growing conversation sends the same history again each turn.
/// The key/value memory of the context still holds that history from the previous turn, and the engine will
/// happily keep it: what has to be worked out is how much of the new prompt is the same as what is there,
/// which is a plain common-prefix length over the token ids.
/// </para>
/// <para>
/// One token is always given back. The decode loop needs logits for the position it is about to sample from,
/// and logits only exist for a position that was part of the last decode, so the reusable length is capped
/// one short of the prompt: even a prompt identical to what is in memory re-evaluates its final token.
/// </para>
/// </remarks>
internal sealed class PrefixCache
{
    private readonly List<int> tokens = new List<int>();

    /// <summary>How many tokens are in the memory.</summary>
    public int Count => tokens.Count;

    /// <summary>How many of a prompt's leading tokens are already in the memory and need not be evaluated.</summary>
    /// <param name="promptTokens">The whole prompt.</param>
    /// <returns>The count, which is always at least one short of the prompt's length.</returns>
    public int ReusableLength(IReadOnlyList<int> promptTokens)
    {
        if (promptTokens == null || promptTokens.Count == 0) return 0;

        int limit = Math.Min(tokens.Count, promptTokens.Count - 1);
        int shared = 0;
        while (shared < limit && tokens[shared] == promptTokens[shared]) shared++;

        return shared;
    }

    /// <summary>Drops everything after a length, matching a removal from the engine's memory.</summary>
    /// <param name="length">How many tokens are left.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative or too large.</exception>
    public void TruncateTo(int length)
    {
        if (length < 0 || length > tokens.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, $"The memory holds {tokens.Count} tokens.");
        }

        tokens.RemoveRange(length, tokens.Count - length);
    }

    /// <summary>Records a token that has been decoded into the memory.</summary>
    /// <param name="token">The token id.</param>
    public void Add(int token)
    {
        tokens.Add(token);
    }

    /// <summary>Records a run of tokens that have been decoded into the memory.</summary>
    /// <param name="decoded">The token ids, in order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="decoded"/> is <see langword="null"/>.</exception>
    public void AddRange(IReadOnlyList<int> decoded)
    {
        if (decoded == null) throw new ArgumentNullException(nameof(decoded));

        for (int i = 0; i < decoded.Count; i++) tokens.Add(decoded[i]);
    }

    /// <summary>Forgets everything, matching a cleared memory.</summary>
    public void Clear()
    {
        tokens.Clear();
    }
}

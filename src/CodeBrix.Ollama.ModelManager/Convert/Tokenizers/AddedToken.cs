using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// One entry of a tokenizer configuration's added-token table: a token the publisher registered by hand, and
/// the two flags that decide what the converted vocabulary calls it.
/// </summary>
internal sealed class AddedToken
{
    internal AddedToken(string content, bool special, bool normalized)
    {
        Content = content;
        Special = special;
        Normalized = normalized;
    }

    /// <summary>The token's text.</summary>
    internal string Content { get; }

    /// <summary>Whether the publisher marked it as a special token, which makes it a control token.</summary>
    internal bool Special { get; }

    /// <summary>Whether the tokenizer normalizes the text before matching it.</summary>
    internal bool Normalized { get; }
}

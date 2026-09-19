namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A token whose TEXT is matched before anything else - a marker like an end-of-sequence token - together
/// with the number it stands for and whether it counts as special.
/// </summary>
/// <remarks>
/// An added token is not reached by merging: it is looked for in the text as it stands, and what it matches
/// is taken out before the surrounding text is cut into pieces. Whether it is SPECIAL decides only whether
/// decoding drops it by default.
/// </remarks>
internal sealed class Gpt2AddedToken
{
    /// <summary>Creates the description of one added token.</summary>
    /// <param name="content">The text it is written as.</param>
    /// <param name="id">The number it stands for.</param>
    /// <param name="isSpecial">Whether it counts as a special token.</param>
    internal Gpt2AddedToken(string content, int id, bool isSpecial)
    {
        Content = content;
        Id = id;
        IsSpecial = isSpecial;
    }

    /// <summary>The text it is written as.</summary>
    internal string Content { get; }

    /// <summary>The number it stands for.</summary>
    internal int Id { get; }

    /// <summary>Whether it counts as a special token, which is what decoding drops by default.</summary>
    internal bool IsSpecial { get; }
}

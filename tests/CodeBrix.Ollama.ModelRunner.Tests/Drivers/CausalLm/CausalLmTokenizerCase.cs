using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// One string and what the published Python tokenizer made of it: the token numbers, the pieces they were
/// merged from, and the text they decode back to with and without the special tokens.
/// </summary>
public sealed class CausalLmTokenizerCase
{
    /// <summary>The string.</summary>
    public string Text { get; set; }

    /// <summary>The token numbers.</summary>
    public IReadOnlyList<int> Ids { get; set; }

    /// <summary>What it decodes back to by default.</summary>
    public string Decoded { get; set; }

    /// <summary>What it decodes back to with the special tokens written out.</summary>
    public string DecodedWithSpecials { get; set; }

    /// <summary>What it decodes back to with the special tokens dropped.</summary>
    public string DecodedWithoutSpecials { get; set; }

    /// <summary>The symbol strings the token numbers stand for, in order.</summary>
    public IReadOnlyList<string> Pieces { get; set; }
}

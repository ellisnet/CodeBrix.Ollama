using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The whole of <c>tokenizer-cases.json</c>: which tokenizer the cases were dumped from, and every case.
/// </summary>
public sealed class CausalLmTokenizerFixture
{
    /// <summary>What the cases were dumped from.</summary>
    public string Tokenizer { get; set; }

    /// <summary>Every case.</summary>
    public IReadOnlyList<CausalLmTokenizerCase> Cases { get; set; }
}

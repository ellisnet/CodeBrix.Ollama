using System.Collections.Generic;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// One prompt as the oracle read and continued it: the token numbers the publisher's tokenizer gave it, and
/// the tokens the runtime generated from them.
/// </summary>
public sealed class CausalLmOraclePrompt
{
    /// <summary>The prompt.</summary>
    public string Text { get; set; }

    /// <summary>The token numbers the publisher's own tokenizer gave it.</summary>
    public IReadOnlyList<int> Ids { get; set; }

    /// <summary>What those token numbers decode back to.</summary>
    public string Decoded { get; set; }

    /// <summary>The tokens the runtime generated.</summary>
    public IReadOnlyList<int> Generated { get; set; }

    /// <summary>What those decode to.</summary>
    public string GeneratedText { get; set; }

    /// <summary>How long reading the prompt took.</summary>
    public double PrefillSeconds { get; set; }

    /// <summary>How long the whole generation took.</summary>
    public double Seconds { get; set; }
}

using System.Collections.Generic;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The whole of what the oracle wrote: which runtime generated, what it made of each prompt, and the most
/// likely token at every position of each sequence it was given.
/// </summary>
public sealed class CausalLmOracleRun
{
    /// <summary>The runtime that generated, and its version.</summary>
    public string Engine { get; set; }

    /// <summary>One entry per prompt.</summary>
    public IReadOnlyList<CausalLmOraclePrompt> Prompts { get; set; }

    /// <summary>The most likely token at every position of each sequence it was given.</summary>
    public IReadOnlyList<IReadOnlyList<int>> TeacherForced { get; set; }
}

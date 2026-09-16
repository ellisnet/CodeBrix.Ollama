using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Controls one generation or chat request: how long it may run, what ends it, how tokens are sampled and
/// whether the output is constrained by a grammar.
/// </summary>
public sealed class GenerationOptions
{
    /// <summary>
    /// The largest number of tokens to generate. <see langword="null"/> (the default) generates until the model
    /// stops or the context is full.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>Strings that end generation when the output reaches them. The stop string itself is not returned. Empty by default.</summary>
    public IList<string> StopSequences { get; } = new List<string>();

    /// <summary>The sampling parameters. <see langword="null"/> (the default) uses <see cref="SamplingOptions"/>' defaults.</summary>
    public SamplingOptions Sampling { get; set; }

    /// <summary>
    /// A GBNF grammar the output must conform to. <see langword="null"/> (the default) leaves the output
    /// unconstrained. Takes precedence over <see cref="JsonSchema"/> and <see cref="JsonMode"/>.
    /// </summary>
    public string Grammar { get; set; }

    /// <summary>
    /// A JSON schema the output must conform to, converted into a grammar. <see langword="null"/> (the default)
    /// means none. Takes precedence over <see cref="JsonMode"/>.
    /// </summary>
    public string JsonSchema { get; set; }

    /// <summary>Whether the output must be a JSON value of any shape. Default <see langword="false"/>.</summary>
    public bool JsonMode { get; set; }
}

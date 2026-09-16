using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp common/json-schema-to-grammar.cpp;

/// <summary>
/// One of the grammar rules the schema converter has ready-made - the JSON primitives and the string
/// formats - together with the other built-in rules it refers to.
/// </summary>
internal sealed class GrammarBuiltinRule
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GrammarBuiltinRule"/> class.
    /// </summary>
    /// <param name="content">The right-hand side of the rule.</param>
    /// <param name="dependencies">The names of the built-in rules it refers to.</param>
    public GrammarBuiltinRule(string content, params string[] dependencies)
    {
        Content = content;
        Dependencies = dependencies ?? new string[0];
    }

    /// <summary>The right-hand side of the rule.</summary>
    public string Content { get; }

    /// <summary>The names of the built-in rules it refers to, which have to be added alongside it.</summary>
    public IReadOnlyList<string> Dependencies { get; }
}

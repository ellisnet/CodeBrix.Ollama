namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Chooses the GBNF grammar text that constrains one request, from the four ways a request can ask for one.
/// </summary>
/// <remarks>
/// The three on <see cref="GenerationOptions"/> are ordered as that type documents them: an explicit
/// grammar wins over a JSON schema, which wins over plain JSON mode. A chat request's
/// <see cref="ResponseFormat"/> comes last of all, because it is the coarsest of the four and an option set
/// explicitly on the request's own generation controls is the more specific instruction.
/// </remarks>
internal static class EngineGrammar
{
    /// <summary>The grammar text for a completion request, or <see langword="null"/> when it is unconstrained.</summary>
    /// <param name="options">The generation options.</param>
    /// <returns>The GBNF text, or <see langword="null"/>.</returns>
    /// <exception cref="GrammarException">A JSON schema was asked for and could not be turned into a grammar.</exception>
    public static string Select(GenerationOptions options)
    {
        if (options == null) return null;

        if (!string.IsNullOrWhiteSpace(options.Grammar)) return options.Grammar;
        if (!string.IsNullOrWhiteSpace(options.JsonSchema)) return JsonSchemaGrammar.FromSchema(options.JsonSchema);
        if (options.JsonMode) return JsonSchemaGrammar.JsonGrammar;

        return null;
    }

    /// <summary>The grammar text for a chat request, or <see langword="null"/> when it is unconstrained.</summary>
    /// <param name="options">The request's generation options, or <see langword="null"/>.</param>
    /// <param name="format">The request's response format, or <see langword="null"/>.</param>
    /// <returns>The GBNF text, or <see langword="null"/>.</returns>
    /// <exception cref="GrammarException">A JSON schema was asked for and could not be turned into a grammar.</exception>
    public static string Select(GenerationOptions options, ResponseFormat format)
    {
        string chosen = Select(options);
        if (chosen != null) return chosen;
        if (format == null) return null;

        if (!string.IsNullOrWhiteSpace(format.JsonSchema)) return JsonSchemaGrammar.FromSchema(format.JsonSchema);
        if (format.IsJson) return JsonSchemaGrammar.JsonGrammar;

        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp common/json-schema-to-grammar.cpp and grammars/json.gbnf;

/// <summary>
/// Turns a JSON schema into the GBNF grammar that holds a model to it, so structured output comes back
/// valid the first time instead of being validated and retried.
/// </summary>
/// <remarks>
/// <para>
/// This is a port of llama.cpp's own schema converter, so the grammar it writes for a schema is the same
/// grammar llama.cpp writes, rule for rule. It covers <c>$ref</c> within the document and the
/// <c>$defs</c>/<c>definitions</c> sections it points into, <c>oneOf</c>, <c>anyOf</c> and <c>allOf</c>,
/// <c>enum</c> and <c>const</c>, strings with <c>minLength</c>, <c>maxLength</c>, <c>pattern</c> and the
/// <c>date</c>, <c>time</c>, <c>date-time</c> and <c>uuid</c> formats, numbers and integers with
/// <c>minimum</c>, <c>maximum</c> and their exclusive forms, booleans, nulls, arrays with <c>items</c>,
/// <c>prefixItems</c>, <c>minItems</c> and <c>maxItems</c>, objects with <c>properties</c>,
/// <c>required</c> and <c>additionalProperties</c>, and the unconstrained "any JSON" fallback.
/// </para>
/// <para>
/// A remote <c>$ref</c> - one pointing at another document over http - is refused rather than fetched,
/// because converting a schema does no I/O.
/// </para>
/// </remarks>
public static class JsonSchemaGrammar
{
    /// <summary>
    /// The grammar that accepts any JSON object, for a caller that wants JSON but has no schema to hold
    /// the model to. This is llama.cpp's own <c>grammars/json.gbnf</c>, verbatim.
    /// </summary>
    public static string JsonGrammar { get; } =
        """
        root   ::= object
        value  ::= object | array | string | number | ("true" | "false" | "null") ws

        object ::=
          "{" ws (
                    string ":" ws value
            ("," ws string ":" ws value)*
          )? "}" ws

        array  ::=
          "[" ws (
                    value
            ("," ws value)*
          )? "]" ws

        string ::=
          "\"" (
            [^"\\\x7F\x00-\x1F] |
            "\\" (["\\bfnrt] | "u" [0-9a-fA-F]{4}) # escapes
          )* "\"" ws

        number ::= ("-"? ([0-9] | [1-9] [0-9]{0,15})) ("." [0-9]+)? ([eE] [-+]? [0-9] [1-9]{0,15})? ws

        # Optional space: by convention, applied in this grammar after literal chars when allowed
        ws ::= | " " | "\n" [ \t]{0,20}

        """;

    /// <summary>
    /// Converts a JSON schema into a GBNF grammar.
    /// </summary>
    /// <param name="jsonSchemaText">The schema, as JSON text.</param>
    /// <returns>The grammar text. Every rule is on its own line and the start rule is <c>root</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jsonSchemaText"/> is <see langword="null"/>.</exception>
    /// <exception cref="GrammarException">The text is not JSON, or the schema uses something the converter cannot express.</exception>
    public static string FromSchema(string jsonSchemaText)
    {
        IReadOnlyList<string> ignored;
        return FromSchema(jsonSchemaText, out ignored);
    }

    /// <summary>
    /// Converts a JSON schema into a GBNF grammar, reporting what the converter had to widen along the way.
    /// </summary>
    /// <param name="jsonSchemaText">The schema, as JSON text.</param>
    /// <param name="warnings">
    /// Receives what the converter had to widen or skip while still producing a usable grammar - a pattern
    /// it cannot express exactly, for instance. Empty for a schema it converts exactly.
    /// </param>
    /// <returns>The grammar text. Every rule is on its own line and the start rule is <c>root</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jsonSchemaText"/> is <see langword="null"/>.</exception>
    /// <exception cref="GrammarException">The text is not JSON, or the schema uses something the converter cannot express.</exception>
    public static string FromSchema(string jsonSchemaText, out IReadOnlyList<string> warnings)
    {
        if (jsonSchemaText == null) { throw new ArgumentNullException(nameof(jsonSchemaText)); }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonSchemaText);
        }
        catch (JsonException exception)
        {
            throw new GrammarException("The JSON schema could not be parsed.", exception);
        }

        using (document)
        {
            return FromSchema(document.RootElement, out warnings);
        }
    }

    /// <summary>
    /// Converts an already parsed JSON schema into a GBNF grammar.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <returns>The grammar text. Every rule is on its own line and the start rule is <c>root</c>.</returns>
    /// <exception cref="GrammarException">The schema uses something the converter cannot express.</exception>
    public static string FromSchema(JsonElement schema)
    {
        IReadOnlyList<string> ignored;
        return FromSchema(schema, out ignored);
    }

    /// <summary>
    /// Converts an already parsed JSON schema into a GBNF grammar, reporting what the converter had to
    /// widen along the way.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <param name="warnings">
    /// Receives what the converter had to widen or skip while still producing a usable grammar - a pattern
    /// it cannot express exactly, for instance. Empty for a schema it converts exactly.
    /// </param>
    /// <returns>The grammar text. Every rule is on its own line and the start rule is <c>root</c>.</returns>
    /// <exception cref="GrammarException">The schema uses something the converter cannot express.</exception>
    public static string FromSchema(JsonElement schema, out IReadOnlyList<string> warnings)
    {
        JsonSchemaConverter converter = new JsonSchemaConverter();
        string grammar = converter.Convert(schema);
        warnings = converter.Warnings;
        return grammar;
    }
}

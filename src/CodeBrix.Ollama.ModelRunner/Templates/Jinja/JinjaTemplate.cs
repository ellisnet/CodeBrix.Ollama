using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A compiled Jinja chat template: the kind of template a GGUF model carries in its
/// <c>tokenizer.chat_template</c> metadata. Parse once, render as often as you like.
/// </summary>
/// <remarks>
/// <para>
/// THE ENVIRONMENT. Templates are rendered the way the Hugging Face transformers library renders them,
/// because that is the behaviour model authors write against: <c>trim_blocks</c> and
/// <c>lstrip_blocks</c> are ON, <c>keep_trailing_newline</c> is OFF (the template source's final
/// newline is dropped), auto-escaping is OFF, the <c>break</c> and <c>continue</c> statements are
/// available, and a missing variable yields the non-strict undefined value rather than failing.
/// </para>
/// <para>
/// THE VALUE MODEL. Variables are ordinary .NET values. A string is a Python string, a
/// <see cref="bool"/> is a Python bool, any integral type is a Python int, any floating-point type is
/// a Python float, and null is Python's <c>None</c> (which renders as the text "None"). A sequence is
/// any <see cref="System.Collections.IList"/> or other <see cref="System.Collections.IEnumerable"/>,
/// and a mapping is any <see cref="IDictionary{TKey,TValue}"/>,
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> or non-generic
/// <see cref="System.Collections.IDictionary"/> whose keys are strings; a
/// <see cref="System.Text.Json.JsonElement"/> is accepted and converted as well. Values are copied and
/// normalized when <see cref="Render"/> is called, so a template that appends to a list cannot change
/// the caller's data, and mapping key order is preserved because that is what <c>| tojson</c> emits.
/// </para>
/// <para>
/// MISSING VALUES. Reading a variable, attribute or item that does not exist yields undefined:
/// it is falsy, it renders as the empty string, <c>is defined</c> is false for it, the
/// <c>default</c> filter replaces it, and <c>x in undefined</c> is false - but iterating it or using it
/// in arithmetic raises <see cref="ChatTemplateException"/>, exactly as Jinja does.
/// </para>
/// <para>
/// THE VARIABLES A CHAT TEMPLATE EXPECTS. The renderer that drives this class supplies
/// <c>messages</c> (a list of mappings with "role" and "content", and optionally
/// "reasoning_content", "tool_calls", "tool_call_id" and "name"; each tool call is
/// <c>{"type": "function", "id": ..., "function": {"name": ..., "arguments": ...}}</c> where
/// "arguments" is a mapping when the model emitted JSON that parses and a string otherwise),
/// <c>tools</c> (a list of <c>{"type": "function", "function": {"name", "description", "parameters"}}</c>),
/// <c>add_generation_prompt</c>, <c>bos_token</c> and <c>eos_token</c>, and <c>enable_thinking</c> when
/// the request asks for a thinking mode. Anything else the caller adds is visible too.
/// </para>
/// </remarks>
public sealed class JinjaTemplate
{
    private readonly IList<JinjaNode> _body;

    private JinjaTemplate(string source, IList<JinjaNode> body)
    {
        Source = source;
        _body = body;
    }

    /// <summary>The template source this instance was parsed from, unchanged.</summary>
    public string Source { get; }

    /// <summary>Parses a Jinja template.</summary>
    /// <param name="source">The template source.</param>
    /// <returns>The compiled template.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ChatTemplateException">
    /// The source is not valid Jinja. The message carries the line and column of the problem.
    /// </exception>
    public static JinjaTemplate Parse(string source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new JinjaTemplate(source, JinjaParser.Parse(source));
    }

    /// <summary>Renders the template.</summary>
    /// <param name="variables">
    /// The variables the template sees. See the remarks on this class for the value model and for the
    /// variables a chat template expects.
    /// </param>
    /// <returns>The rendered text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variables"/> is null.</exception>
    /// <exception cref="ChatTemplateException">
    /// The template failed while rendering - an undefined value was iterated or added, a filter was
    /// misused, or the template itself called <c>raise_exception()</c>, whose message is passed
    /// through unchanged.
    /// </exception>
    public string Render(IReadOnlyDictionary<string, object> variables)
    {
        if (variables == null)
        {
            throw new ArgumentNullException(nameof(variables));
        }

        try
        {
            return new JinjaRenderer(variables).Render(_body);
        }
        catch (JinjaLoopSignal)
        {
            throw new ChatTemplateException(
                "Jinja template error: a 'break' or 'continue' was used outside a loop.");
        }
    }
}

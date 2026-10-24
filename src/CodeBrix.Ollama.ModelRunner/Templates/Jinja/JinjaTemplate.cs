using System;
using System.Collections.Generic;
using System.Threading;

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
/// normalized when <see cref="Render(IReadOnlyDictionary{string, object})"/> is called, so a template that appends to a list cannot change
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
/// <para>
/// LIMITS. A template is held to a <see cref="TemplateLimits"/> - the defaults, or the ones given to
/// <see cref="Parse(string, TemplateLimits)"/> - so a hostile one fails with
/// <see cref="ChatTemplateException"/> instead of hanging the render, exhausting memory or overflowing the
/// stack.
/// </para>
/// </remarks>
public sealed class JinjaTemplate
{
    private readonly IList<JinjaNode> _body;

    private readonly TemplateLimits _limits;

    private JinjaTemplate(string source, IList<JinjaNode> body, TemplateLimits limits)
    {
        Source = source;
        _body = body;
        _limits = limits;
    }

    /// <summary>The template source this instance was parsed from, unchanged.</summary>
    public string Source { get; }

    /// <summary>Parses a Jinja template under the default <see cref="TemplateLimits"/>.</summary>
    /// <param name="source">The template source.</param>
    /// <returns>The compiled template.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ChatTemplateException">
    /// The source is not valid Jinja, or nests deeper than <see cref="TemplateLimits.MaxNestingDepth"/>. The
    /// message carries the line and column of the problem.
    /// </exception>
    public static JinjaTemplate Parse(string source) => Parse(source, null);

    /// <summary>Parses a Jinja template under the given limits, which every render of it is held to.</summary>
    /// <param name="source">The template source.</param>
    /// <param name="limits">
    /// The limits, or <see langword="null"/> for the defaults. They are copied, so later changes to the
    /// object do not reach this template.
    /// </param>
    /// <returns>The compiled template.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentException">A limit is out of range.</exception>
    /// <exception cref="ChatTemplateException">
    /// The source is not valid Jinja, or nests deeper than <see cref="TemplateLimits.MaxNestingDepth"/>. The
    /// message carries the line and column of the problem.
    /// </exception>
    public static JinjaTemplate Parse(string source, TemplateLimits limits)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        TemplateLimits copy = TemplateLimits.ValidatedCopy(limits, nameof(limits));
        return new JinjaTemplate(source, JinjaParser.Parse(source, copy.MaxNestingDepth), copy);
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
    /// misused, a <see cref="TemplateLimits"/> limit was exceeded, or the template itself called
    /// <c>raise_exception()</c>, whose message is passed through unchanged.
    /// </exception>
    public string Render(IReadOnlyDictionary<string, object> variables)
        => Render(variables, CancellationToken.None);

    /// <summary>Renders the template, checking the token as the render goes.</summary>
    /// <param name="variables">
    /// The variables the template sees. See the remarks on this class for the value model and for the
    /// variables a chat template expects.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the render.</param>
    /// <returns>The rendered text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variables"/> is null.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="ChatTemplateException">
    /// The template failed while rendering - an undefined value was iterated or added, a filter was
    /// misused, a <see cref="TemplateLimits"/> limit was exceeded, or the template itself called
    /// <c>raise_exception()</c>, whose message is passed through unchanged.
    /// </exception>
    public string Render(IReadOnlyDictionary<string, object> variables, CancellationToken cancellationToken)
    {
        if (variables == null)
        {
            throw new ArgumentNullException(nameof(variables));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using (TemplateBudget.Open(_limits, cancellationToken))
        {
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
}

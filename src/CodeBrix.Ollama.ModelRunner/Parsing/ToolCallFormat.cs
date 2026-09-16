using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama tools/template.go;

/// <summary>
/// How a particular model announces a tool call in its output: the literal it writes before the call and,
/// where there is one, the literal it writes after.
/// </summary>
/// <remarks>
/// <para>
/// Ollama learns the prefix from the model's own chat template, by finding the branch the template takes
/// for tool calls and reading the first literal text it emits - <c>&lt;tool_call&gt;</c> for Qwen and
/// Hermes, <c>[TOOL_CALLS] [</c> for Mistral, a DeepSeek begin marker, and so on. When the template emits
/// no literal at all, and writes a bare JSON object or array instead, the prefix falls back to <c>{</c> or
/// <c>[</c>, which tells <see cref="ToolCallParser"/> to look for bare JSON at the very start of the
/// output and to treat anything else as content.
/// </para>
/// <para>
/// <see cref="Prefix"/> is therefore never empty. <see cref="IsBareJson"/> distinguishes the two
/// fallbacks from a real literal.
/// </para>
/// </remarks>
public sealed class ToolCallFormat
{
    /// <summary>
    /// The opening and closing literals recognized in a Jinja tool-calling template, most specific first.
    /// The closing literal is only reported when it also appears in the template.
    /// </summary>
    private static readonly (string Prefix, string Suffix)[] JinjaMarkers =
    {
        ("<tool_call>", "</tool_call>"),
        ("<|tool▁calls▁begin|>", "<|tool▁calls▁end|>"),
        ("<|tool_call|>", "<|/tool_call|>"),
        ("<|start_tool_call|>", "<|end_tool_call|>"),
        ("[TOOL_CALLS]", "[/TOOL_CALLS]"),
        ("<|python_tag|>", "<|eom_id|>"),
        ("<function=", "</function>"),
        ("<toolcall>", "</toolcall>"),
        ("functools[", null),
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolCallFormat"/> class.
    /// </summary>
    /// <param name="prefix">
    /// The literal the model writes before a tool call. <see langword="null"/> or empty means the model
    /// writes a bare JSON object, and is stored as <c>{</c>.
    /// </param>
    /// <param name="suffix">
    /// The literal the model writes after a tool call, or <see langword="null"/> when there is none.
    /// Recorded for callers that want it; the parser does not need it, because a tool call ends where its
    /// JSON ends.
    /// </param>
    public ToolCallFormat(string prefix, string suffix = null)
    {
        Prefix = string.IsNullOrEmpty(prefix) ? "{" : prefix;
        Suffix = suffix;
    }

    /// <summary>
    /// The literal the model writes before a tool call. Never empty: <c>{</c> or <c>[</c> mean the model
    /// writes bare JSON with no literal of its own.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// The literal the model writes after a tool call, or <see langword="null"/> when there is none.
    /// </summary>
    public string Suffix { get; }

    /// <summary>
    /// <see langword="true"/> when the model writes bare JSON rather than a literal of its own, so a tool
    /// call is only recognized when the output starts with the JSON.
    /// </summary>
    public bool IsBareJson => Prefix == "{" || Prefix == "[";

    /// <summary>
    /// The format for a model whose tool-call literal is not known: bare JSON detection only.
    /// </summary>
    public static ToolCallFormat Auto { get; } = new ToolCallFormat("{");

    /// <summary>
    /// Derives the format from a Go chat template, the way Ollama does.
    /// </summary>
    /// <remarks>
    /// The heuristic: find the <c>{{ if .ToolCalls }}</c> branch, take the first literal text it emits
    /// before any nested action, cut it at the first <c>{</c> so a JSON object body is not mistaken for a
    /// literal, and trim it. An empty result means the template emits bare JSON, which becomes the
    /// <c>{</c> prefix.
    /// </remarks>
    /// <param name="goTemplateText">The Go template source. <see langword="null"/> is treated as empty.</param>
    /// <returns>The format. Never <see langword="null"/>.</returns>
    public static ToolCallFormat FromGoTemplateText(string goTemplateText)
    {
        IList<GoTemplateOutlineNode> root = GoTemplateOutline.Parse(goTemplateText);

        GoTemplateOutlineNode toolCallNode = FindToolCallNode(root);
        if (toolCallNode == null)
        {
            return Auto;
        }

        GoTemplateOutlineNode textNode = FindTextNode(toolCallNode.Body);
        if (textNode == null)
        {
            return Auto;
        }

        string tag = textNode.Text.Replace("\r\n", "\n");

        //Anything from the first `{` onwards is the tool call's own JSON, not the literal in front of it.
        int brace = tag.IndexOf('{');
        if (brace >= 0)
        {
            tag = tag.Substring(0, brace);
        }

        tag = tag.Trim();
        if (tag.Length == 0)
        {
            return Auto;
        }

        return new ToolCallFormat(tag, FindSuffixAfterToolCall(toolCallNode));
    }

    /// <summary>
    /// Derives the format from the text a chat template produced for a message carrying one synthetic tool
    /// call.
    /// </summary>
    /// <remarks>
    /// This is the dialect-independent route: render a message with a single made-up call and hand the
    /// result here. The prefix is everything before the call's JSON, and the suffix is everything after it.
    /// It can differ from <see cref="FromGoTemplateText"/> for a template that writes a per-call literal
    /// as well as a per-block one - DeepSeek's begin markers, for instance - because the rendered text runs
    /// the two together while the template's own structure separates them. Prefer
    /// <see cref="FromGoTemplateText"/> when the template source is to hand.
    /// </remarks>
    /// <param name="renderedSyntheticToolCall">
    /// The rendered text. <see langword="null"/> or text with no JSON object in it yields
    /// <see cref="Auto"/>.
    /// </param>
    /// <returns>The format. Never <see langword="null"/>.</returns>
    public static ToolCallFormat FromRenderedToolCall(string renderedSyntheticToolCall)
    {
        string rendered = (renderedSyntheticToolCall ?? string.Empty).Replace("\r\n", "\n");

        int brace = rendered.IndexOf('{');
        if (brace < 0)
        {
            return Auto;
        }

        string prefix = rendered.Substring(0, brace).Trim();

        int lastBrace = rendered.LastIndexOf('}');
        string suffix = lastBrace >= 0 && lastBrace + 1 < rendered.Length
            ? rendered.Substring(lastBrace + 1).Trim()
            : string.Empty;

        return new ToolCallFormat(prefix, suffix.Length == 0 ? null : suffix);
    }

    /// <summary>
    /// Derives the format from a Jinja chat template by looking for the literals the known families write.
    /// </summary>
    /// <remarks>
    /// Recognized, in this order: <c>&lt;tool_call&gt;</c>/<c>&lt;/tool_call&gt;</c> (Qwen 2.5, Qwen 3,
    /// Qwen 3.5, Hermes, and most templates that borrowed from them), the DeepSeek tool-calls begin and end
    /// markers, <c>&lt;|tool_call|&gt;</c>, <c>&lt;|start_tool_call|&gt;</c>, <c>[TOOL_CALLS]</c>
    /// (Mistral), <c>&lt;|python_tag|&gt;</c> (Llama 3.1 and 3.2), <c>&lt;function=</c> (the Llama
    /// function syntax), <c>&lt;toolcall&gt;</c> and <c>functools[</c> (Firefunction). A suffix is only
    /// reported when its literal is in the template too. When none of them appears the result is
    /// <see cref="Auto"/>.
    /// </remarks>
    /// <param name="jinjaTemplateText">The Jinja template source. <see langword="null"/> is treated as empty.</param>
    /// <returns>The format. Never <see langword="null"/>.</returns>
    public static ToolCallFormat FromJinjaTemplateText(string jinjaTemplateText)
    {
        string template = jinjaTemplateText ?? string.Empty;

        foreach ((string prefix, string suffix) in JinjaMarkers)
        {
            if (!template.Contains(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            bool hasSuffix = suffix != null && template.Contains(suffix, StringComparison.Ordinal);
            return new ToolCallFormat(prefix, hasSuffix ? suffix : null);
        }

        return Auto;
    }

    /// <summary>
    /// Finds the <c>if</c> block whose condition mentions <c>ToolCalls</c>, searching nested blocks too.
    /// </summary>
    /// <param name="nodes">The nodes to search.</param>
    /// <returns>The block, or <see langword="null"/> when the template has none.</returns>
    private static GoTemplateOutlineNode FindToolCallNode(IList<GoTemplateOutlineNode> nodes)
    {
        foreach (GoTemplateOutlineNode node in nodes)
        {
            switch (node.Kind)
            {
                case GoTemplateOutlineKind.If:
                {
                    if (MentionsToolCalls(node))
                    {
                        return node;
                    }

                    GoTemplateOutlineNode found = FindToolCallNode(node.Body);
                    if (found != null)
                    {
                        return found;
                    }

                    if (node.ElseBody != null)
                    {
                        found = FindToolCallNode(node.ElseBody);
                        if (found != null)
                        {
                            return found;
                        }
                    }

                    break;
                }

                case GoTemplateOutlineKind.Range:
                case GoTemplateOutlineKind.With:
                {
                    GoTemplateOutlineNode found = FindToolCallNode(node.Body);
                    if (found != null)
                    {
                        return found;
                    }

                    if (node.ElseBody != null)
                    {
                        found = FindToolCallNode(node.ElseBody);
                        if (found != null)
                        {
                            return found;
                        }
                    }

                    break;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reports whether a node's pipeline mentions the <c>ToolCalls</c> field at any position.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool MentionsToolCalls(GoTemplateOutlineNode node)
    {
        foreach (IList<string> field in node.PipelineFields)
        {
            foreach (string identifier in field)
            {
                if (string.Equals(identifier, "ToolCalls", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the first literal text a branch emits, depth first, stopping at the first action so text that
    /// only appears after the tool call itself is not mistaken for the prefix.
    /// </summary>
    /// <param name="nodes">The nodes to search.</param>
    /// <returns>The text node, or <see langword="null"/> when there is none.</returns>
    private static GoTemplateOutlineNode FindTextNode(IList<GoTemplateOutlineNode> nodes)
    {
        foreach (GoTemplateOutlineNode node in nodes)
        {
            switch (node.Kind)
            {
                case GoTemplateOutlineKind.Text:
                    if (node.Text.Trim().Length == 0)
                    {
                        //Whitespace-only runs are skipped.
                        continue;
                    }

                    return node;

                case GoTemplateOutlineKind.If:
                case GoTemplateOutlineKind.Range:
                case GoTemplateOutlineKind.With:
                {
                    GoTemplateOutlineNode text = FindTextNode(node.Body);
                    if (text != null)
                    {
                        return text;
                    }

                    if (node.ElseBody != null)
                    {
                        text = FindTextNode(node.ElseBody);
                        if (text != null)
                        {
                            return text;
                        }
                    }

                    return null;
                }

                case GoTemplateOutlineKind.Action:
                    return null;

                default:
                    //A `{{ template }}` reference carries no text of its own; keep looking.
                    continue;
            }
        }

        return null;
    }

    /// <summary>
    /// Takes the literal the template emits after the tool call, which is the last literal text run of the
    /// tool-call branch when that run is not also the prefix.
    /// </summary>
    /// <param name="toolCallNode">The tool-call branch.</param>
    /// <returns>The suffix, or <see langword="null"/> when there is none.</returns>
    private static string FindSuffixAfterToolCall(GoTemplateOutlineNode toolCallNode)
    {
        string suffix = null;
        CollectLastText(toolCallNode.Body, ref suffix);

        if (suffix == null)
        {
            return null;
        }

        int brace = suffix.LastIndexOf('}');
        if (brace >= 0)
        {
            suffix = suffix.Substring(brace + 1);
        }

        suffix = suffix.Trim();
        return suffix.Length == 0 ? null : suffix;
    }

    /// <summary>
    /// Records the last non-whitespace literal text run in a branch, descending into nested blocks.
    /// </summary>
    /// <param name="nodes">The nodes to walk.</param>
    /// <param name="last">The last literal seen so far.</param>
    private static void CollectLastText(IList<GoTemplateOutlineNode> nodes, ref string last)
    {
        foreach (GoTemplateOutlineNode node in nodes)
        {
            if (node.Kind == GoTemplateOutlineKind.Text)
            {
                if (node.Text.Trim().Length != 0)
                {
                    last = node.Text;
                }
            }
            else if (node.Kind == GoTemplateOutlineKind.If
                     || node.Kind == GoTemplateOutlineKind.Range
                     || node.Kind == GoTemplateOutlineKind.With)
            {
                CollectLastText(node.Body, ref last);
                if (node.ElseBody != null)
                {
                    CollectLastText(node.ElseBody, ref last);
                }
            }
        }
    }
}

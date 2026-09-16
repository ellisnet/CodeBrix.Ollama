using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama thinking/template.go;

/// <summary>
/// Works out, from a model's chat template, whether the model writes its reasoning between tags and what
/// those tags are, so a <see cref="ThinkingParser"/> can be set up for it.
/// </summary>
/// <remarks>
/// <para>
/// Ollama infers the tags from a Go chat template structurally: it looks for the <c>{{ .Thinking }}</c>
/// reference inside the loop over <c>{{ .Messages }}</c> and takes the literal text immediately before and
/// after it. <see cref="InferGoTemplateTags"/> is that heuristic. A Jinja template is inspected by string
/// search instead, because a Jinja template's reasoning tags are always written as literals;
/// <see cref="InferJinjaTemplateTags"/> lists what it recognizes.
/// </para>
/// <para>
/// All of the members here are pure string work and do no I/O.
/// </para>
/// </remarks>
public static class ThinkingTags
{
    /// <summary>
    /// The opening and closing literals recognized in a Jinja chat template, most specific first. Each is
    /// only accepted when both halves appear in the template.
    /// </summary>
    private static readonly (string Opening, string Closing)[] JinjaTagPairs =
    {
        ("<think>", "</think>"),
        ("<|START_THINKING|>", "<|END_THINKING|>"),
        ("<|channel|>analysis<|message|>", "<|end|>"),
        ("<seed:think>", "</seed:think>"),
        ("◁think▷", "◁/think▷"),
        ("<reasoning>", "</reasoning>"),
        ("<thought>", "</thought>"),
    };

    /// <summary>
    /// Reports whether a raw chat template declares tagged reasoning output.
    /// </summary>
    /// <remarks>
    /// A template that emits a paired <c>&lt;think&gt;</c> block counts, and so does one that strips a
    /// previous turn's reasoning by splitting on <c>&lt;/think&gt;</c>. A template that carries reasoning
    /// in a separate <c>reasoning_content</c> field does not, because there are no tags in the output to
    /// parse.
    /// </remarks>
    /// <param name="chatTemplate">The template source. <see langword="null"/> is treated as empty.</param>
    /// <returns><see langword="true"/> when the model writes tagged reasoning.</returns>
    public static bool TemplateSupportsThinking(string chatTemplate)
    {
        string template = chatTemplate ?? string.Empty;

        if (template.Contains("<think>", StringComparison.Ordinal)
            && template.Contains("</think>", StringComparison.Ordinal))
        {
            return true;
        }

        return (template.Contains("content.split('</think>')", StringComparison.Ordinal)
                || template.Contains("content.split(\"</think>\")", StringComparison.Ordinal))
               && !template.Contains("reasoning_content", StringComparison.Ordinal)
               && !template.Contains("<SPECIAL_12>", StringComparison.Ordinal);
    }

    /// <summary>
    /// Infers the reasoning tags from a chat template of either dialect, choosing the Go heuristic for a Go
    /// template and the string search for anything else.
    /// </summary>
    /// <remarks>
    /// The string search is also the fallback when the Go heuristic finds nothing, which is how Ollama
    /// itself decides whether a template supports thinking: the structural walk and the plain text search
    /// are separate signals and either one counts.
    /// </remarks>
    /// <param name="chatTemplate">The template source. <see langword="null"/> is treated as empty.</param>
    /// <param name="openingTag">Receives the opening tag, or an empty string when none was found.</param>
    /// <param name="closingTag">Receives the closing tag, or an empty string when none was found.</param>
    /// <returns><see langword="true"/> when both tags were found.</returns>
    public static bool Infer(string chatTemplate, out string openingTag, out string closingTag)
    {
        string template = chatTemplate ?? string.Empty;

        if (LooksLikeGoTemplate(template) && InferGoTemplateTags(template, out openingTag, out closingTag))
        {
            return true;
        }

        return InferJinjaTemplateTags(template, out openingTag, out closingTag);
    }

    /// <summary>
    /// Infers the reasoning tags from a Go chat template, the way Ollama does.
    /// </summary>
    /// <remarks>
    /// The heuristic: find a reference to <c>.Thinking</c> that sits inside a <c>range</c> over
    /// <c>.Messages</c>, go up to the innermost enclosing list of nodes, and take that list's first and
    /// last literal text runs - trimmed - as the opening and closing tags. A reference outside such a loop,
    /// for instance the <c>{{ if .Thinking }}/think{{ end }}</c> switch many templates put in the system
    /// block, is ignored.
    /// </remarks>
    /// <param name="goTemplateText">The Go template source. <see langword="null"/> is treated as empty.</param>
    /// <param name="openingTag">Receives the opening tag, or an empty string when none was found.</param>
    /// <param name="closingTag">Receives the closing tag, or an empty string when none was found.</param>
    /// <returns><see langword="true"/> when both tags were found.</returns>
    public static bool InferGoTemplateTags(string goTemplateText, out string openingTag, out string closingTag)
    {
        IList<GoTemplateOutlineNode> root = GoTemplateOutline.Parse(goTemplateText);

        string opening = string.Empty;
        string closing = string.Empty;

        List<IList<GoTemplateOutlineNode>> lists = new List<IList<GoTemplateOutlineNode>>();
        List<GoTemplateOutlineNode> ranges = new List<GoTemplateOutlineNode>();
        VisitForThinking(root, lists, ranges, ref opening, ref closing);

        openingTag = opening;
        closingTag = closing;
        return opening.Length > 0 && closing.Length > 0;
    }

    /// <summary>
    /// Infers the reasoning tags from a Jinja chat template by looking for the literals the known families
    /// write.
    /// </summary>
    /// <remarks>
    /// Recognized, in this order: <c>&lt;think&gt;</c>/<c>&lt;/think&gt;</c> (Qwen 3 and 3.5, DeepSeek-R1,
    /// Hermes and most others), <c>&lt;|START_THINKING|&gt;</c>/<c>&lt;|END_THINKING|&gt;</c> (Command R),
    /// <c>&lt;|channel|&gt;analysis&lt;|message|&gt;</c>/<c>&lt;|end|&gt;</c> (the gpt-oss harmony
    /// format), <c>&lt;seed:think&gt;</c>/<c>&lt;/seed:think&gt;</c> (Seed-OSS), the Kimi think markers,
    /// <c>&lt;reasoning&gt;</c>/<c>&lt;/reasoning&gt;</c>, and <c>&lt;thought&gt;</c>/<c>&lt;/thought&gt;</c>
    /// (the Gemma-style pair). A pair is only accepted when both halves appear in the template. As a last
    /// resort a template that merely strips a previous turn's reasoning with
    /// <c>content.split('&lt;/think&gt;')</c> yields the <c>&lt;think&gt;</c> pair.
    /// </remarks>
    /// <param name="jinjaTemplateText">The Jinja template source. <see langword="null"/> is treated as empty.</param>
    /// <param name="openingTag">Receives the opening tag, or an empty string when none was found.</param>
    /// <param name="closingTag">Receives the closing tag, or an empty string when none was found.</param>
    /// <returns><see langword="true"/> when both tags were found.</returns>
    public static bool InferJinjaTemplateTags(string jinjaTemplateText, out string openingTag,
        out string closingTag)
    {
        string template = jinjaTemplateText ?? string.Empty;

        foreach ((string opening, string closing) in JinjaTagPairs)
        {
            if (template.Contains(opening, StringComparison.Ordinal)
                && template.Contains(closing, StringComparison.Ordinal))
            {
                openingTag = opening;
                closingTag = closing;
                return true;
            }
        }

        if (TemplateSupportsThinking(template))
        {
            openingTag = "<think>";
            closingTag = "</think>";
            return true;
        }

        openingTag = string.Empty;
        closingTag = string.Empty;
        return false;
    }

    /// <summary>
    /// Reports whether a rendered prompt already ends with the opening tag, which means the model's first
    /// token is reasoning and a <see cref="ThinkingParser"/> for it must start inside the thinking block.
    /// </summary>
    /// <param name="renderedPrompt">The prompt as rendered for the model. <see langword="null"/> is treated as empty.</param>
    /// <param name="openingTag">The opening tag.</param>
    /// <returns><see langword="true"/> when the trailing whitespace-trimmed prompt ends with the tag.</returns>
    public static bool PromptEndsWithOpeningTag(string renderedPrompt, string openingTag)
    {
        if (string.IsNullOrEmpty(openingTag))
        {
            return false;
        }

        string prompt = (renderedPrompt ?? string.Empty).TrimEnd();
        return prompt.EndsWith(openingTag, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reports whether a template looks like a Go chat template rather than a Jinja one.
    /// </summary>
    /// <param name="template">The template source.</param>
    /// <returns><see langword="true"/> when the Go heuristic should be tried first.</returns>
    private static bool LooksLikeGoTemplate(string template)
    {
        if (!template.Contains("{{", StringComparison.Ordinal))
        {
            return false;
        }

        //Jinja spells its loops `{% for %}` and its fields in lower case; a Go chat template refers to the
        //capitalised fields Ollama passes it.
        return !template.Contains("{%", StringComparison.Ordinal)
               && (template.Contains(".Messages", StringComparison.Ordinal)
                   || template.Contains(".Thinking", StringComparison.Ordinal)
                   || template.Contains(".Prompt", StringComparison.Ordinal)
                   || template.Contains(".Content", StringComparison.Ordinal));
    }

    /// <summary>
    /// Walks a list of nodes looking for <c>.Thinking</c> references, keeping the enclosing lists and
    /// ranges so the tags can be read off the innermost list once one is found.
    /// </summary>
    /// <param name="nodes">The list being visited.</param>
    /// <param name="lists">The stack of enclosing lists.</param>
    /// <param name="ranges">The stack of enclosing range nodes.</param>
    /// <param name="openingTag">The opening tag found so far.</param>
    /// <param name="closingTag">The closing tag found so far.</param>
    private static void VisitForThinking(IList<GoTemplateOutlineNode> nodes,
        List<IList<GoTemplateOutlineNode>> lists, List<GoTemplateOutlineNode> ranges,
        ref string openingTag, ref string closingTag)
    {
        lists.Add(nodes);

        foreach (GoTemplateOutlineNode node in nodes)
        {
            switch (node.Kind)
            {
                case GoTemplateOutlineKind.Text:
                    break;

                case GoTemplateOutlineKind.Action:
                case GoTemplateOutlineKind.Template:
                    CheckThinkingFields(node, lists, ranges, ref openingTag, ref closingTag);
                    break;

                default:
                {
                    bool isRange = node.Kind == GoTemplateOutlineKind.Range;
                    if (isRange)
                    {
                        ranges.Add(node);
                    }

                    //Go visits a branch node's pipeline before its body, and the last match wins.
                    CheckThinkingFields(node, lists, ranges, ref openingTag, ref closingTag);
                    VisitForThinking(node.Body, lists, ranges, ref openingTag, ref closingTag);
                    if (node.ElseBody != null)
                    {
                        VisitForThinking(node.ElseBody, lists, ranges, ref openingTag, ref closingTag);
                    }

                    if (isRange)
                    {
                        ranges.RemoveAt(ranges.Count - 1);
                    }

                    break;
                }
            }
        }

        lists.RemoveAt(lists.Count - 1);
    }

    /// <summary>
    /// Applies the tag heuristic to every <c>.Thinking</c> reference in one node's pipeline.
    /// </summary>
    /// <param name="node">The node whose pipeline is being checked.</param>
    /// <param name="lists">The stack of enclosing lists.</param>
    /// <param name="ranges">The stack of enclosing range nodes.</param>
    /// <param name="openingTag">The opening tag found so far.</param>
    /// <param name="closingTag">The closing tag found so far.</param>
    private static void CheckThinkingFields(GoTemplateOutlineNode node,
        List<IList<GoTemplateOutlineNode>> lists, List<GoTemplateOutlineNode> ranges,
        ref string openingTag, ref string closingTag)
    {
        foreach (IList<string> field in node.PipelineFields)
        {
            if (field.Count == 0 || !string.Equals(field[0], "Thinking", StringComparison.Ordinal))
            {
                continue;
            }

            if (ranges.Count == 0 || !RangeUsesField(ranges[ranges.Count - 1], "Messages"))
            {
                continue;
            }

            if (lists.Count == 0)
            {
                continue;
            }

            IList<GoTemplateOutlineNode> list = lists[lists.Count - 1];
            if (list.Count == 0)
            {
                continue;
            }

            GoTemplateOutlineNode first = list[0];
            if (first.Kind == GoTemplateOutlineKind.Text)
            {
                openingTag = first.Text.Trim();
            }

            GoTemplateOutlineNode last = list[list.Count - 1];
            if (last.Kind == GoTemplateOutlineKind.Text)
            {
                closingTag = last.Text.Trim();
            }
        }
    }

    /// <summary>
    /// Reports whether a range node's own pipeline mentions a field.
    /// </summary>
    /// <param name="rangeNode">The range node.</param>
    /// <param name="field">The field name to look for.</param>
    /// <returns><see langword="true"/> when the pipeline mentions it.</returns>
    private static bool RangeUsesField(GoTemplateOutlineNode rangeNode, string field)
    {
        foreach (IList<string> candidate in rangeNode.PipelineFields)
        {
            if (candidate.Count > 0 && string.Equals(candidate[0], field, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

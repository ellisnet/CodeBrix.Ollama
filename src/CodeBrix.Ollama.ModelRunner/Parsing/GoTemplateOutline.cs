using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go text/template/parse (structural subset, reference only);

/// <summary>
/// Builds the structural outline of a Go text/template: its literal text runs, the nesting of its
/// <c>if</c>, <c>range</c> and <c>with</c> blocks, and the field references each pipeline mentions.
/// </summary>
/// <remarks>
/// <para>
/// Ollama reads two things out of a model's Go chat template by walking the parse tree Go's own
/// <c>text/template</c> package builds: the pair of tags that surround reasoning
/// (<see cref="ThinkingTags"/>) and the literal a tool call is prefixed with
/// (<see cref="ToolCallFormat"/>). This class recovers just enough of that tree for both heuristics
/// without pulling in a template engine, so the parsers stay independent of the renderer.
/// </para>
/// <para>
/// Trim markers are applied exactly as Go's lexer applies them: <c>{{-</c> followed by a space eats the
/// whitespace that precedes the action, a space followed by <c>-}}</c> eats the whitespace that follows
/// it, and a text run that trimming empties is not emitted at all. Quoted strings inside a pipeline are
/// skipped, so a <c>}}</c> inside a literal does not end the action.
/// </para>
/// </remarks>
internal static class GoTemplateOutline
{
    /// <summary>The characters Go's lexer treats as trimmable space around an action.</summary>
    private const string SpaceCharacters = " \t\r\n";

    /// <summary>
    /// Parses a Go template into its outline.
    /// </summary>
    /// <param name="templateText">The template source. <see langword="null"/> is treated as empty.</param>
    /// <returns>The root list of nodes, in source order. Never <see langword="null"/>.</returns>
    public static IList<GoTemplateOutlineNode> Parse(string templateText)
    {
        string source = templateText ?? string.Empty;
        int position = 0;
        string closer;
        string closerPipeline;
        return ParseList(source, ref position, true, out closer, out closerPipeline);
    }

    /// <summary>
    /// Parses nodes until the list is closed by <c>end</c>, <c>else</c>, <c>else if</c> or the end of the
    /// source.
    /// </summary>
    /// <param name="source">The template source.</param>
    /// <param name="position">The read position, advanced past whatever closed the list.</param>
    /// <param name="isRoot">
    /// <see langword="true"/> for the template's own top-level list, where there is no block for an
    /// <c>end</c> or an <c>else</c> to close and a stray one is ignored rather than ending the list.
    /// </param>
    /// <param name="closer">Receives "end", "else", "elseif" or <see langword="null"/> at end of source.</param>
    /// <param name="closerPipeline">Receives the pipeline of an "elseif" closer.</param>
    /// <returns>The nodes of the list, in source order.</returns>
    private static IList<GoTemplateOutlineNode> ParseList(string source, ref int position, bool isRoot,
        out string closer, out string closerPipeline)
    {
        List<GoTemplateOutlineNode> nodes = new List<GoTemplateOutlineNode>();
        closer = null;
        closerPipeline = null;

        while (position < source.Length)
        {
            int open = source.IndexOf("{{", position, StringComparison.Ordinal);
            if (open < 0)
            {
                AppendText(nodes, source.Substring(position));
                position = source.Length;
                return nodes;
            }

            bool trimLeft = HasLeftTrimMarker(source, open + 2);
            string text = source.Substring(position, open - position);
            if (trimLeft)
            {
                text = text.TrimEnd(SpaceCharacters.ToCharArray());
            }

            AppendText(nodes, text);

            int inner = open + 2 + (trimLeft ? 1 : 0);
            int close = FindActionEnd(source, inner);
            if (close < 0)
            {
                //An unterminated action; Go would refuse the template, and there is nothing more to read.
                position = source.Length;
                return nodes;
            }

            string action = source.Substring(inner, close - inner);
            position = close + 2;

            if (HasRightTrimMarker(action))
            {
                action = action.Substring(0, action.Length - 1);
                position = SkipSpace(source, position);
            }

            action = action.Trim();

            if (action.StartsWith("/*", StringComparison.Ordinal))
            {
                //A comment produces no node.
                continue;
            }

            string keyword = FirstWord(action);
            string pipeline = action.Substring(keyword.Length).Trim();

            switch (keyword)
            {
                case "end":
                    if (isRoot)
                    {
                        //A stray `{{end}}` closing nothing. Go refuses such a template outright; the
                        //outline drops the action and keeps reading, because truncating the template
                        //would lose the very literals the heuristics are here to find.
                        continue;
                    }

                    closer = "end";
                    return nodes;

                case "else":
                    if (isRoot)
                    {
                        //A stray `{{else}}`, dropped for the same reason.
                        continue;
                    }

                    if (pipeline.StartsWith("if", StringComparison.Ordinal)
                        && (pipeline.Length == 2 || IsSpace(pipeline[2]) || pipeline[2] == '('))
                    {
                        closer = "elseif";
                        closerPipeline = pipeline.Substring(2).Trim();
                    }
                    else
                    {
                        closer = "else";
                    }

                    return nodes;

                case "if":
                    nodes.Add(ParseBranch(source, ref position, GoTemplateOutlineKind.If, pipeline));
                    break;

                case "range":
                    nodes.Add(ParseBranch(source, ref position, GoTemplateOutlineKind.Range, pipeline));
                    break;

                case "with":
                    nodes.Add(ParseBranch(source, ref position, GoTemplateOutlineKind.With, pipeline));
                    break;

                case "define":
                case "block":
                    nodes.Add(ParseBranch(source, ref position, GoTemplateOutlineKind.Template, pipeline));
                    break;

                case "template":
                    nodes.Add(CreateNode(GoTemplateOutlineKind.Template, pipeline));
                    break;

                default:
                    nodes.Add(CreateNode(GoTemplateOutlineKind.Action, action));
                    break;
            }
        }

        return nodes;
    }

    /// <summary>
    /// Parses a block node and the body - and any else body - that follows it.
    /// </summary>
    /// <param name="source">The template source.</param>
    /// <param name="position">The read position, advanced past the block's <c>end</c>.</param>
    /// <param name="kind">The kind of block.</param>
    /// <param name="pipeline">The block's pipeline source.</param>
    /// <returns>The block node.</returns>
    private static GoTemplateOutlineNode ParseBranch(string source, ref int position,
        GoTemplateOutlineKind kind, string pipeline)
    {
        GoTemplateOutlineNode node = CreateNode(kind, pipeline);

        string closer;
        string closerPipeline;
        IList<GoTemplateOutlineNode> body =
            ParseList(source, ref position, false, out closer, out closerPipeline);
        foreach (GoTemplateOutlineNode child in body)
        {
            node.Body.Add(child);
        }

        if (closer == "else")
        {
            string innerCloser;
            string innerCloserPipeline;
            node.ElseBody =
                ParseList(source, ref position, false, out innerCloser, out innerCloserPipeline);
        }
        else if (closer == "elseif")
        {
            //Go rewrites `{{else if x}}` into an else list holding one if node, and that node swallows the
            //single `{{end}}` that closes the whole chain.
            GoTemplateOutlineNode inner =
                ParseBranch(source, ref position, GoTemplateOutlineKind.If, closerPipeline);
            node.ElseBody = new List<GoTemplateOutlineNode> { inner };
        }

        return node;
    }

    /// <summary>
    /// Creates a node of the given kind and records the field references in its pipeline.
    /// </summary>
    /// <param name="kind">The kind of node.</param>
    /// <param name="pipeline">The pipeline source.</param>
    /// <returns>The node.</returns>
    private static GoTemplateOutlineNode CreateNode(GoTemplateOutlineKind kind, string pipeline)
    {
        GoTemplateOutlineNode node = new GoTemplateOutlineNode { Kind = kind, Pipeline = pipeline };
        foreach (IList<string> field in ExtractFields(pipeline))
        {
            node.PipelineFields.Add(field);
        }

        return node;
    }

    /// <summary>
    /// Adds a text node, unless the text is empty. Go's lexer emits no node for an empty text run, and the
    /// thinking heuristic depends on that.
    /// </summary>
    /// <param name="nodes">The list being built.</param>
    /// <param name="text">The literal text.</param>
    private static void AppendText(IList<GoTemplateOutlineNode> nodes, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            nodes.Add(new GoTemplateOutlineNode { Kind = GoTemplateOutlineKind.Text, Text = text });
        }
    }

    /// <summary>
    /// Finds the <c>}}</c> that ends an action, skipping quoted strings.
    /// </summary>
    /// <param name="source">The template source.</param>
    /// <param name="start">The first character after the opening delimiter.</param>
    /// <returns>The index of the closing delimiter, or -1 when there is none.</returns>
    private static int FindActionEnd(string source, int start)
    {
        int i = start;

        //Go's lexer skips the space a trim marker leaves behind before it looks for a comment, so
        //`{{- /* ... */ -}}` is as much a comment as `{{/* ... */}}` is. Skipping it here matters:
        //an unrecognized comment is scanned for quotes and delimiters, and an apostrophe or a `}}`
        //inside one would then end the action in the wrong place, or never.
        while (i < source.Length && IsSpace(source[i]))
        {
            i++;
        }

        if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
        {
            int commentEnd = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
            if (commentEnd < 0)
            {
                return -1;
            }

            i = commentEnd + 2;
        }

        while (i < source.Length)
        {
            char c = source[i];
            if (c == '"' || c == '\'')
            {
                i++;
                while (i < source.Length && source[i] != c)
                {
                    i += source[i] == '\\' ? 2 : 1;
                }

                i++;
                continue;
            }

            if (c == '`')
            {
                i++;
                while (i < source.Length && source[i] != '`')
                {
                    i++;
                }

                i++;
                continue;
            }

            if (c == '}' && i + 1 < source.Length && source[i + 1] == '}')
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    /// <summary>
    /// Reports whether an opening delimiter carries a left trim marker, which Go spells as a hyphen
    /// followed by a space.
    /// </summary>
    /// <param name="source">The template source.</param>
    /// <param name="index">The first character after the opening delimiter.</param>
    /// <returns><see langword="true"/> when the preceding text should be right-trimmed.</returns>
    private static bool HasLeftTrimMarker(string source, int index)
    {
        return index + 1 < source.Length && source[index] == '-' && IsSpace(source[index + 1]);
    }

    /// <summary>
    /// Reports whether an action's text carries a right trim marker, which Go spells as a space followed
    /// by a hyphen immediately before the closing delimiter.
    /// </summary>
    /// <param name="action">The raw text between the delimiters.</param>
    /// <returns><see langword="true"/> when the following text should be left-trimmed.</returns>
    private static bool HasRightTrimMarker(string action)
    {
        return action.Length >= 2 && action[action.Length - 1] == '-' && IsSpace(action[action.Length - 2]);
    }

    /// <summary>
    /// Advances past the run of trimmable space at a position.
    /// </summary>
    /// <param name="source">The template source.</param>
    /// <param name="position">Where to start.</param>
    /// <returns>The position of the first character that is not trimmable space.</returns>
    private static int SkipSpace(string source, int position)
    {
        while (position < source.Length && IsSpace(source[position]))
        {
            position++;
        }

        return position;
    }

    /// <summary>
    /// Reports whether a character is one Go trims around an action.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> for space, tab, carriage return and newline.</returns>
    private static bool IsSpace(char c)
    {
        return SpaceCharacters.IndexOf(c) >= 0;
    }

    /// <summary>
    /// Returns the leading identifier of an action, which is the keyword when there is one.
    /// </summary>
    /// <param name="action">The trimmed action text.</param>
    /// <returns>The first word, possibly empty.</returns>
    private static string FirstWord(string action)
    {
        int i = 0;
        while (i < action.Length && (char.IsLetter(action[i]) || action[i] == '_'))
        {
            i++;
        }

        if (i < action.Length && !IsSpace(action[i]) && action[i] != '(' && action[i] != '"')
        {
            //Something like `.Content` or `$x` is not a keyword at all.
            return string.Empty;
        }

        return action.Substring(0, i);
    }

    /// <summary>
    /// Collects the field references of a pipeline, each split on its dots.
    /// </summary>
    /// <param name="pipeline">The pipeline source.</param>
    /// <returns>The field references, in source order.</returns>
    private static IList<IList<string>> ExtractFields(string pipeline)
    {
        List<IList<string>> fields = new List<IList<string>>();
        if (string.IsNullOrEmpty(pipeline))
        {
            return fields;
        }

        int i = 0;
        char previous = '\0';
        while (i < pipeline.Length)
        {
            char c = pipeline[i];

            if (c == '"' || c == '\'')
            {
                i++;
                while (i < pipeline.Length && pipeline[i] != c)
                {
                    i += pipeline[i] == '\\' ? 2 : 1;
                }

                i++;
                previous = c;
                continue;
            }

            if (c == '`')
            {
                i++;
                while (i < pipeline.Length && pipeline[i] != '`')
                {
                    i++;
                }

                i++;
                previous = c;
                continue;
            }

            if (c == '.' && IsFieldStart(previous) && i + 1 < pipeline.Length
                && (char.IsLetter(pipeline[i + 1]) || pipeline[i + 1] == '_'))
            {
                int start = i;
                i++;
                while (i < pipeline.Length
                       && (char.IsLetterOrDigit(pipeline[i]) || pipeline[i] == '_' || pipeline[i] == '.'))
                {
                    i++;
                }

                string token = pipeline.Substring(start, i - start);
                List<string> identifiers = new List<string>();
                foreach (string part in token.Split('.'))
                {
                    if (part.Length > 0)
                    {
                        identifiers.Add(part);
                    }
                }

                if (identifiers.Count > 0)
                {
                    fields.Add(identifiers);
                }

                previous = pipeline[i - 1];
                continue;
            }

            previous = c;
            i++;
        }

        return fields;
    }

    /// <summary>
    /// Reports whether a dot at this point starts a field reference rather than continuing a variable such
    /// as <c>$.Messages</c>, which Go treats as a variable and not a field.
    /// </summary>
    /// <param name="previous">The character before the dot, or the null character at the start.</param>
    /// <returns><see langword="true"/> when a field reference may start here.</returns>
    private static bool IsFieldStart(char previous)
    {
        return previous == '\0' || IsSpace(previous) || previous == '(' || previous == '|'
               || previous == ',' || previous == ':' || previous == '=';
    }
}

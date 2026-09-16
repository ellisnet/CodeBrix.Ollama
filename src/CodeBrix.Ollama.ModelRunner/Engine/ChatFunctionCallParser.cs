using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Pulls tool calls out of the output of a model whose template teaches it to write a call as nested XML
/// tags rather than as JSON.
/// </summary>
/// <remarks>
/// <para>
/// Qwen 3.5 is the model this library is built for and it is one of these. Its template instructs the model
/// to answer with a block of the shape
/// <c>&lt;tool_call&gt;&lt;function=NAME&gt;&lt;parameter=P&gt;VALUE&lt;/parameter&gt;&lt;/function&gt;&lt;/tool_call&gt;</c>,
/// and it renders a previous turn's calls back into exactly that shape. <see cref="ToolCallParser"/> reads
/// the JSON form that Qwen 2.5, Hermes, Mistral and most others use, and cannot read this one: it looks for
/// a JSON object after the literal and there is none.
/// </para>
/// <para>
/// A parameter's value is text, so its JSON type has to be recovered. The tool's own parameter schema
/// decides: a parameter the schema declares as a string stays a string even when its value happens to look
/// like a number, and any other parameter's value is used as JSON when it parses as JSON and as a string
/// when it does not. That is the inverse of what the template does when it renders a call back, which
/// writes a mapping or a sequence as JSON and everything else as its plain text.
/// </para>
/// <para>
/// A block naming a tool that was not offered is not a call; its text is handed back as content, tags and
/// all, rather than being lost. A block that turns out to hold JSON after all is read as JSON, so a model
/// that mixes the two conventions is still understood. One parser instance handles one response.
/// </para>
/// <para>
/// A closing literal is not trusted on sight, because a parameter's value is free text and may contain one.
/// A <c>&lt;/parameter&gt;</c> ends a value only when what follows it, once whitespace is skipped, is the
/// next parameter or the end of the function; a <c>&lt;/tool_call&gt;</c> ends a block only when what comes
/// before it ends with <c>&lt;/function&gt;</c>. Text held back waiting for a literal that never arrives is
/// capped, and once it passes the cap the whole of it is handed back as content rather than growing without
/// bound.
/// </para>
/// <para>
/// Two offered tools of the same name are one tool: the first wins and the later ones are ignored, which is
/// what reading the name back out of the model's output has to do anyway.
/// </para>
/// </remarks>
internal sealed class ChatFunctionCallParser
{
    private const string DefaultOpenTag = "<tool_call>";
    private const string DefaultCloseTag = "</tool_call>";
    private const string FunctionOpen = "<function=";
    private const string ParameterOpen = "<parameter=";
    private const string ParameterClose = "</parameter>";
    private const string FunctionClose = "</function>";

    // A megabyte of text with no closing literal in it is not a tool call any more, whatever the model
    // thought it was writing, and the caller is owed the words rather than an ever-growing buffer.
    private const int MaximumHeldCharacters = 1024 * 1024;

    private static readonly IReadOnlyList<ToolCall> NoCalls = Array.Empty<ToolCall>();

    private readonly IReadOnlyList<ToolDefinition> tools;
    private readonly Dictionary<string, HashSet<string>> stringParameters =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    private readonly string openTag;
    private readonly string closeTag;
    private readonly StringBuilder held = new StringBuilder();

    private bool inside;

    /// <summary>Creates a parser for one response.</summary>
    /// <param name="format">The literals the model's template taught it, or <see langword="null"/> for the defaults.</param>
    /// <param name="offeredTools">The tools that were offered. Only these names become calls; <see langword="null"/> means none.</param>
    public ChatFunctionCallParser(ToolCallFormat format, IReadOnlyList<ToolDefinition> offeredTools)
    {
        tools = offeredTools ?? Array.Empty<ToolDefinition>();

        openTag = format == null || format.IsBareJson ? DefaultOpenTag : format.Prefix;
        closeTag = format == null || string.IsNullOrEmpty(format.Suffix) ? DefaultCloseTag : format.Suffix;

        foreach (ToolDefinition tool in tools)
        {
            if (tool == null || string.IsNullOrEmpty(tool.Name)) continue;
            if (stringParameters.ContainsKey(tool.Name)) continue;

            stringParameters[tool.Name] = ReadStringParameters(tool.ParametersJsonSchema);
        }
    }

    /// <summary>The number of calls read so far.</summary>
    public int CallCount { get; private set; }

    /// <summary>
    /// Reports whether a chat template teaches the nested-tag call syntax rather than the JSON one.
    /// </summary>
    /// <param name="templateText">The template source. <see langword="null"/> is treated as empty.</param>
    /// <returns><see langword="true"/> when the template writes a call as nested tags.</returns>
    public static bool TemplateUsesFunctionSyntax(string templateText)
    {
        string template = templateText ?? string.Empty;

        return template.Contains(FunctionOpen, StringComparison.Ordinal)
               && template.Contains(ParameterOpen, StringComparison.Ordinal);
    }

    /// <summary>Takes the next chunk of output and returns the calls it completed and the content to emit.</summary>
    /// <param name="chunk">The next piece of the model's output. <see langword="null"/> is treated as empty.</param>
    /// <returns>The completed calls - empty when there are none - and the content text to emit now.</returns>
    public (IReadOnlyList<ToolCall> calls, string content) AddContent(string chunk)
    {
        string text = held.ToString() + (chunk ?? string.Empty);
        held.Clear();

        StringBuilder content = new StringBuilder();
        List<ToolCall> calls = null;

        while (true)
        {
            if (!inside)
            {
                int index = text.IndexOf(openTag, StringComparison.Ordinal);
                if (index >= 0)
                {
                    content.Append(text, 0, index);
                    text = text.Substring(index + openTag.Length);
                    inside = true;
                    continue;
                }

                int hold = PartialSuffixLength(text, openTag);
                content.Append(text, 0, text.Length - hold);
                if (hold > 0) held.Append(text, text.Length - hold, hold);
                break;
            }

            int end = FindBlockEnd(text);
            if (end < 0)
            {
                held.Append(text);

                if (held.Length > MaximumHeldCharacters)
                {
                    content.Append(openTag).Append(held.ToString());
                    held.Clear();
                    inside = false;
                }

                break;
            }

            string block = text.Substring(0, end);
            text = text.Substring(end + closeTag.Length);
            inside = false;

            ToolCall call = ParseBlock(block);
            if (call == null)
            {
                content.Append(openTag).Append(block).Append(closeTag);
                continue;
            }

            if (calls == null) calls = new List<ToolCall>();
            calls.Add(call);
            CallCount++;
        }

        return (calls == null ? NoCalls : calls, content.ToString());
    }

    /// <summary>Ends the stream and returns whatever text was held back and never became a call.</summary>
    /// <returns>The remaining content text, usually empty.</returns>
    /// <remarks>
    /// A block the model started and never finished is dropped: it is a half-written tool call, not
    /// something to show anyone. Text held back because it might have been the start of the opening literal
    /// is content after all, and is returned.
    /// </remarks>
    public string Flush()
    {
        string remainder = inside ? string.Empty : held.ToString();
        held.Clear();
        inside = false;
        return remainder;
    }

    /// <summary>Where the block the parser is inside really ends.</summary>
    /// <param name="text">The text since the opening literal.</param>
    /// <returns>The index of the closing literal, or -1 when none of the ones present ends the block.</returns>
    /// <remarks>
    /// A closing literal inside a parameter's value is not the end of the block. The one that is comes right
    /// after the function's own closing tag, so an occurrence is accepted when the text before it ends with
    /// that tag - or when there is no function tag before it at all, which is the JSON form of a block and
    /// the plain text of one that is not a call.
    /// </remarks>
    private int FindBlockEnd(string text)
    {
        int search = 0;

        while (true)
        {
            int index = text.IndexOf(closeTag, search, StringComparison.Ordinal);
            if (index < 0) return -1;

            if (text.IndexOf(FunctionOpen, 0, index, StringComparison.Ordinal) < 0) return index;

            string before = text.Substring(0, index).TrimEnd();
            if (before.EndsWith(FunctionClose, StringComparison.Ordinal)) return index;

            search = index + closeTag.Length;
        }
    }

    /// <summary>Where one parameter's value ends.</summary>
    /// <param name="body">The block's text.</param>
    /// <param name="start">The index just past the parameter's opening tag.</param>
    /// <returns>The index of the closing literal, or -1 when there is none at all.</returns>
    /// <remarks>
    /// The first <c>&lt;/parameter&gt;</c> that is followed, once whitespace is skipped, by the next
    /// parameter or by the end of the function is the one that ends the value; a value containing the
    /// literal is therefore kept whole. A block in which no occurrence qualifies is malformed, and the first
    /// occurrence is used so that something of it still comes back.
    /// </remarks>
    private static int FindParameterEnd(string body, int start)
    {
        int search = start;
        int first = -1;

        while (true)
        {
            int index = body.IndexOf(ParameterClose, search, StringComparison.Ordinal);
            if (index < 0) return first;

            if (first < 0) first = index;

            int after = index + ParameterClose.Length;
            while (after < body.Length && char.IsWhiteSpace(body[after])) after++;

            if (after >= body.Length
                || string.CompareOrdinal(body, after, ParameterOpen, 0, ParameterOpen.Length) == 0
                || string.CompareOrdinal(body, after, FunctionClose, 0, FunctionClose.Length) == 0)
            {
                return index;
            }

            search = index + ParameterClose.Length;
        }
    }

    /// <summary>The length of the longest suffix of the text that is a proper prefix of the tag.</summary>
    /// <param name="text">The text.</param>
    /// <param name="tag">The tag.</param>
    /// <returns>The length, which is zero when no suffix could start the tag.</returns>
    private static int PartialSuffixLength(string text, string tag)
    {
        int most = Math.Min(text.Length, tag.Length - 1);

        for (int length = most; length > 0; length--)
        {
            if (string.CompareOrdinal(text, text.Length - length, tag, 0, length) == 0) return length;
        }

        return 0;
    }

    /// <summary>The names of a tool's parameters that its schema declares as strings.</summary>
    /// <param name="schemaJson">The tool's parameter schema, as JSON text.</param>
    /// <returns>The names, which is empty when the schema says nothing useful.</returns>
    private static HashSet<string> ReadStringParameters(string schemaJson)
    {
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(schemaJson)) return names;

        try
        {
            using (JsonDocument document = JsonDocument.Parse(schemaJson))
            {
                JsonElement properties;
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("properties", out properties)
                    || properties.ValueKind != JsonValueKind.Object)
                {
                    return names;
                }

                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    JsonElement type;
                    if (property.Value.ValueKind != JsonValueKind.Object
                        || !property.Value.TryGetProperty("type", out type))
                    {
                        continue;
                    }

                    if (type.ValueKind == JsonValueKind.String
                        && string.Equals(type.GetString(), "string", StringComparison.Ordinal))
                    {
                        names.Add(property.Name);
                    }
                }
            }
        }
        catch (JsonException)
        {
            //A schema this library cannot read tells it nothing about the types; the value's own spelling
            //then decides, which is the same rule as for a parameter the schema does not mention.
        }

        return names;
    }

    /// <summary>Reads one block of text from between the opening and closing literals as a call.</summary>
    /// <param name="block">The text between the literals.</param>
    /// <returns>The call, or <see langword="null"/> when the block is not one.</returns>
    private ToolCall ParseBlock(string block)
    {
        string body = (block ?? string.Empty).Trim();
        if (body.Length == 0) return null;

        if (body[0] == '{') return ParseJsonBlock(body);

        int open = body.IndexOf(FunctionOpen, StringComparison.Ordinal);
        if (open < 0) return null;

        int nameStart = open + FunctionOpen.Length;
        int nameEnd = body.IndexOf('>', nameStart);
        if (nameEnd < 0) return null;

        string name = body.Substring(nameStart, nameEnd - nameStart).Trim();
        ToolDefinition tool = FindTool(name);
        if (tool == null) return null;

        StringBuilder arguments = new StringBuilder("{");
        bool first = true;
        int cursor = nameEnd + 1;

        while (true)
        {
            int parameter = body.IndexOf(ParameterOpen, cursor, StringComparison.Ordinal);
            if (parameter < 0) break;

            int valueNameStart = parameter + ParameterOpen.Length;
            int valueNameEnd = body.IndexOf('>', valueNameStart);
            if (valueNameEnd < 0) break;

            string parameterName = body.Substring(valueNameStart, valueNameEnd - valueNameStart).Trim();
            int valueEnd = FindParameterEnd(body, valueNameEnd + 1);

            string value = valueEnd < 0
                ? body.Substring(valueNameEnd + 1)
                : body.Substring(valueNameEnd + 1, valueEnd - valueNameEnd - 1);

            if (!first) arguments.Append(',');
            first = false;

            arguments.Append(CompactJson.DumpString(parameterName));
            arguments.Append(':');
            arguments.Append(ValueAsJson(tool, parameterName, value.Trim()));

            if (valueEnd < 0) break;
            cursor = valueEnd + ParameterClose.Length;
        }

        arguments.Append('}');

        return new ToolCall { Name = tool.Name, ArgumentsJson = arguments.ToString() };
    }

    /// <summary>Reads a block that turned out to hold a JSON call after all.</summary>
    /// <param name="body">The block's text, already trimmed.</param>
    /// <returns>The call, or <see langword="null"/> when it is not one.</returns>
    private ToolCall ParseJsonBlock(string body)
    {
        try
        {
            using (JsonDocument document = JsonDocument.Parse(body))
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                JsonElement function;
                if (root.TryGetProperty("function", out function) && function.ValueKind == JsonValueKind.Object)
                {
                    root = function;
                }

                JsonElement name;
                if (!root.TryGetProperty("name", out name) || name.ValueKind != JsonValueKind.String) return null;

                ToolDefinition tool = FindTool(name.GetString());
                if (tool == null) return null;

                string arguments = "{}";
                JsonElement value;
                if (root.TryGetProperty("arguments", out value))
                {
                    if (value.ValueKind == JsonValueKind.String) arguments = value.GetString();
                    else if (value.ValueKind != JsonValueKind.Undefined) arguments = CompactJson.Dump(value);
                }

                return new ToolCall { Name = tool.Name, ArgumentsJson = arguments };
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The JSON text for one parameter's value.</summary>
    /// <param name="tool">The tool being called.</param>
    /// <param name="parameterName">The parameter's name.</param>
    /// <param name="value">The value as the model wrote it.</param>
    /// <returns>The JSON text.</returns>
    private string ValueAsJson(ToolDefinition tool, string parameterName, string value)
    {
        HashSet<string> strings;
        if (stringParameters.TryGetValue(tool.Name, out strings) && strings.Contains(parameterName))
        {
            return CompactJson.DumpString(value);
        }

        try
        {
            using (JsonDocument document = JsonDocument.Parse(value))
            {
                return CompactJson.Dump(document.RootElement);
            }
        }
        catch (JsonException)
        {
            return CompactJson.DumpString(value);
        }
    }

    /// <summary>The offered tool of a name, or <see langword="null"/> when none was offered.</summary>
    /// <param name="name">The name the model wrote.</param>
    /// <returns>The tool, or <see langword="null"/>.</returns>
    private ToolDefinition FindTool(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        foreach (ToolDefinition tool in tools)
        {
            if (tool != null && string.Equals(tool.Name, name, StringComparison.Ordinal)) return tool;
        }

        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama tools/tools.go;

/// <summary>
/// Pulls tool calls out of a model's output stream as it arrives, separating them from the text the user
/// should see.
/// </summary>
/// <remarks>
/// <para>
/// A model announces a tool call with the literal its chat template taught it - <c>&lt;tool_call&gt;</c>,
/// <c>[TOOL_CALLS] [</c>, a DeepSeek begin marker - and then writes a JSON object carrying the tool's name
/// and its arguments. <see cref="ToolCallFormat"/> works out the literal; this class buffers output until
/// it sees the literal, emits everything before it as content, and then reads JSON objects out of what
/// follows, one call at a time, as each object completes.
/// </para>
/// <para>
/// For a model whose template writes bare JSON with no literal of its own
/// (<see cref="ToolCallFormat.IsBareJson"/>), a call is only recognized when the JSON is the first
/// non-whitespace thing in the output; anything else, including a model that merely happens to write a
/// brace, is emitted as content.
/// </para>
/// <para>
/// A JSON object only becomes a call when it names one of the tools that were offered, so a model writing
/// about a tool it was not given produces content rather than a call, and a name that is still only half
/// written is held back until the next chunk settles which tool it is. Arguments are re-serialised as
/// compact JSON text into <see cref="ToolCall.ArgumentsJson"/>.
/// </para>
/// <para>
/// One parser instance handles one response. It is not thread-safe.
/// </para>
/// </remarks>
public sealed class ToolCallParser
{
    /// <summary>
    /// How much held-back text a parser that has seen the tag will keep before deciding that what follows
    /// it was never a tool call after all.
    /// </summary>
    private const int MaximumBufferLength = 1024 * 1024;

    private static readonly IReadOnlyList<ToolCall> NoCalls = new ToolCall[0];

    private readonly IReadOnlyList<ToolDefinition> _tools;
    private readonly string _tag;

    //The buffer grows one chunk at a time and is trimmed from the front, so it is held as a builder and
    //materialized only when a search needs a string, at most once per chunk.
    private readonly StringBuilder _builder = new StringBuilder();
    private string _bufferText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolCallParser"/> class.
    /// </summary>
    /// <param name="format">How the model announces a tool call. <see langword="null"/> means <see cref="ToolCallFormat.Auto"/>.</param>
    /// <param name="tools">The tools that were offered to the model. Only these names are recognized; <see langword="null"/> means none were offered.</param>
    public ToolCallParser(ToolCallFormat format, IReadOnlyList<ToolDefinition> tools)
    {
        Format = format ?? ToolCallFormat.Auto;
        _tag = Format.Prefix;
        _tools = tools ?? new ToolDefinition[0];
    }

    /// <summary>How the model announces a tool call.</summary>
    public ToolCallFormat Format { get; }

    /// <summary>Where the parser has got to in the stream.</summary>
    public ToolCallParserState State { get; private set; } = ToolCallParserState.LookingForTag;

    /// <summary>The number of calls emitted so far.</summary>
    public int CallCount { get; internal set; }

    /// <summary>The text held back so far, waiting for more output to settle what it is.</summary>
    internal string Buffer
    {
        get
        {
            if (_bufferText == null)
            {
                _bufferText = _builder.ToString();
            }

            return _bufferText;
        }

        set
        {
            _builder.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                _builder.Append(value);
            }

            _bufferText = value ?? string.Empty;
        }
    }

    /// <summary>
    /// Takes the next chunk of model output and returns the tool calls that are now complete along with
    /// the content text that can be emitted now.
    /// </summary>
    /// <param name="chunk">The next piece of the model's output. <see langword="null"/> is treated as empty.</param>
    /// <returns>
    /// A tuple of the completed calls - empty when there are none - and the content text to emit now.
    /// </returns>
    public (IReadOnlyList<ToolCall> calls, string content) AddContent(string chunk)
    {
        string text = chunk ?? string.Empty;

        if (State == ToolCallParserState.Done)
        {
            return (NoCalls, text);
        }

        Append(text);

        string content = string.Empty;
        List<ToolCall> calls = null;

        if (State == ToolCallParserState.LookingForTag)
        {
            bool found;
            int index = FindTag(out found);

            if (index == -1)
            {
                content = TakeBuffer();
            }
            else
            {
                content = Buffer.Substring(0, index);
                RemovePrefix(index);
            }

            //For a model whose tag is a bare brace or bracket, only output that starts with the JSON can be
            //a tool call; anything before it means this is ordinary text.
            if (Format.IsBareJson && content.Trim().Length != 0)
            {
                State = ToolCallParserState.Done;
                return (NoCalls, content + TakeBuffer());
            }

            if (!found)
            {
                return (NoCalls, content);
            }

            State = ToolCallParserState.ToolCalling;
        }

        while (true)
        {
            ToolCall call = ParseToolCall();
            if (call == null)
            {
                break;
            }

            if (calls == null)
            {
                calls = new List<ToolCall>();
            }

            calls.Add(call);
        }

        if (IsComplete())
        {
            State = ToolCallParserState.Done;
            content += TakeBuffer();
        }
        else if (State == ToolCallParserState.ToolCalling && _builder.Length > MaximumBufferLength)
        {
            //A tag the model never followed with a parseable call would otherwise pin an unbounded amount
            //of output in memory, so past the cap the held-back text is treated as content after all and
            //the parser goes back to looking for a tag.
            content += TakeBuffer();
            State = ToolCallParserState.LookingForTag;
        }

        return (calls == null ? NoCalls : calls, content);
    }

    /// <summary>
    /// Ends the stream and returns any buffered text that should still be shown to the user.
    /// </summary>
    /// <remarks>
    /// This is empty once a call has been parsed, because what follows a call belongs to the call's own
    /// framing. When no call was ever parsed the buffer turns out to have been content all along - bare
    /// JSON that named no offered tool, a half-written tag the stream ended on, or a tag the model never
    /// followed with usable JSON - and it is returned rather than dropped. Ollama drops the last two;
    /// returning them is a deliberate deviation, because dropped text is a silently truncated answer.
    /// </remarks>
    /// <returns>The remaining content text, possibly empty.</returns>
    public string Flush()
    {
        string remainder = TakeBuffer();
        return CallCount > 0 ? string.Empty : remainder;
    }

    /// <summary>
    /// Adds text to the held-back buffer.
    /// </summary>
    /// <param name="text">The text to add.</param>
    private void Append(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _builder.Append(text);
        _bufferText = null;
    }

    /// <summary>
    /// Drops the first characters of the held-back buffer.
    /// </summary>
    /// <param name="count">How many characters to drop.</param>
    private void RemovePrefix(int count)
    {
        if (count <= 0)
        {
            return;
        }

        _builder.Remove(0, Math.Min(count, _builder.Length));
        _bufferText = null;
    }

    /// <summary>
    /// Returns the whole held-back buffer and empties it.
    /// </summary>
    /// <returns>What the buffer held, possibly empty.</returns>
    private string TakeBuffer()
    {
        string text = Buffer;
        _builder.Clear();
        _bufferText = string.Empty;
        return text;
    }

    /// <summary>
    /// Looks for the tool-call tag in the buffer.
    /// </summary>
    /// <param name="found">Receives <see langword="true"/> when the whole tag is present.</param>
    /// <returns>
    /// The index the tag starts at, the index a partial tag starts at when the buffer ends part way
    /// through one, or -1 when there is no sign of the tag at all.
    /// </returns>
    internal int FindTag(out bool found)
    {
        string buffer = Buffer;

        //A complete tag anywhere in the buffer wins.
        int index = buffer.IndexOf(_tag, StringComparison.Ordinal);
        if (index > -1)
        {
            found = true;
            return index;
        }

        found = false;

        //Otherwise the buffer may end part way through one, which has to be held back. The comparison is
        //made in place so that a long buffer costs no substring of its own for every candidate length.
        int longest = Math.Min(buffer.Length, _tag.Length);
        for (int i = longest; i > 0; i--)
        {
            if (string.CompareOrdinal(buffer, buffer.Length - i, _tag, 0, i) == 0)
            {
                return buffer.Length - i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reads the next complete tool call out of the buffer, advancing past it.
    /// </summary>
    /// <returns>The call, or <see langword="null"/> when the buffer holds no complete call.</returns>
    private ToolCall ParseToolCall()
    {
        string buffer = Buffer;

        int end;
        ToolDefinition tool = FindTool(_tools, buffer, out end);
        if (tool == null)
        {
            return null;
        }

        int argumentsEnd;
        string arguments = FindArguments(tool.Name, buffer, out argumentsEnd);
        if (arguments == null)
        {
            return null;
        }

        if (argumentsEnd > end)
        {
            end = argumentsEnd;
        }

        ToolCall call = new ToolCall
        {
            Name = tool.Name,
            ArgumentsJson = arguments,
        };

        CallCount++;
        RemovePrefix(end);
        return call;
    }

    /// <summary>
    /// Reports whether the buffer holds a balanced bare JSON object or array, which is how a parser with a
    /// bare brace or bracket tag knows it has seen everything there was to see.
    /// </summary>
    /// <remarks>
    /// Only <c>{</c> and <c>[</c> tags can be finished this way. A model with a literal tag keeps parsing
    /// until the stream ends, because a brace pair on its own is no guarantee the calls are over.
    /// </remarks>
    /// <returns><see langword="true"/> when the buffer is complete.</returns>
    internal bool IsComplete()
    {
        char open;
        char close;
        if (_tag == "{")
        {
            open = '{';
            close = '}';
        }
        else if (_tag == "[")
        {
            open = '[';
            close = ']';
        }
        else
        {
            return false;
        }

        int count = 0;
        bool inString = false;
        bool escaped = false;
        string buffer = Buffer;
        foreach (char c in buffer)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == open)
            {
                count++;
            }
            else if (c == close)
            {
                count--;
                if (count == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the first offered tool whose name appears in the buffer.
    /// </summary>
    /// <remarks>
    /// Nothing is returned while the buffer ends in text that could still grow into a longer tool name, so
    /// <c>say_hello</c> is not matched when <c>say_hello_world</c> is also on offer and the stream has only
    /// reached the shorter spelling. Where two names start at the same place, the longer one wins.
    /// </remarks>
    /// <param name="tools">The offered tools.</param>
    /// <param name="buffer">The text to search.</param>
    /// <param name="end">Receives the index just past the matched name, or zero when nothing matched.</param>
    /// <returns>The tool, or <see langword="null"/> when none matched yet.</returns>
    internal static ToolDefinition FindTool(IReadOnlyList<ToolDefinition> tools, string buffer, out int end)
    {
        end = 0;
        if (string.IsNullOrEmpty(buffer))
        {
            return null;
        }

        string longest = string.Empty;
        foreach (ToolDefinition tool in tools)
        {
            if (!string.IsNullOrEmpty(tool?.Name) && tool.Name.Length > longest.Length)
            {
                longest = tool.Name;
            }
        }

        //Hold back while the tail of the buffer is still a prefix of a longer name. The tail is compared
        //in place rather than cut out, so nothing is allocated per candidate length.
        int tailLimit = Math.Min(buffer.Length, longest.Length);
        for (int i = 1; i <= tailLimit; i++)
        {
            foreach (ToolDefinition tool in tools)
            {
                string name = tool?.Name;
                if (!string.IsNullOrEmpty(name) && i < name.Length
                    && string.CompareOrdinal(buffer, buffer.Length - i, name, 0, i) == 0)
                {
                    return null;
                }
            }
        }

        ToolDefinition found = null;
        int start = -1;
        int foundEnd = -1;

        foreach (ToolDefinition tool in tools)
        {
            string name = tool?.Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            int position = buffer.IndexOf(name, StringComparison.Ordinal);
            if (position == -1)
            {
                continue;
            }

            if (start != -1)
            {
                if (position > start)
                {
                    continue;
                }

                if (position == start && name.Length <= found.Name.Length)
                {
                    continue;
                }
            }

            found = tool;
            start = position;
            foundEnd = position + name.Length;
        }

        if (found != null)
        {
            end = foundEnd;
            return found;
        }

        return null;
    }

    /// <summary>
    /// Finds the first JSON object in the buffer that looks like the arguments of a call to a tool.
    /// </summary>
    /// <remarks>
    /// An object carrying a <c>name</c> gives up its <c>arguments</c> or <c>parameters</c> member, which
    /// may itself be an object or a JSON object written as a string. An object keyed by the tool's own name
    /// gives up that member. Otherwise the search goes into nested objects and arrays, and an object that
    /// matches none of those shapes is taken to be the arguments itself. A tool call written with a name
    /// but no arguments at all - <c>{"name": "get_conditions"}</c> - is not recognized, which is a
    /// limitation this port keeps from Ollama.
    /// </remarks>
    /// <param name="toolName">The name of the tool whose arguments are wanted.</param>
    /// <param name="buffer">The text to search.</param>
    /// <param name="end">Receives the index just past the closing brace of the object that was read, or zero.</param>
    /// <returns>The arguments as compact JSON text, or <see langword="null"/> when none were found.</returns>
    internal static string FindArguments(string toolName, string buffer, out int end)
    {
        end = 0;
        if (string.IsNullOrEmpty(buffer))
        {
            return null;
        }

        int start = -1;
        int braces = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < buffer.Length; i++)
        {
            char c = buffer[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '{')
            {
                if (braces == 0)
                {
                    start = i;
                }

                braces++;
            }
            else if (c == '}')
            {
                braces--;
                if (braces == 0 && start != -1)
                {
                    string objectText = buffer.Substring(start, i - start + 1);
                    JsonDocument document = TryParseObject(objectText);
                    if (document == null)
                    {
                        //Not a valid object, so keep looking.
                        start = -1;
                        continue;
                    }

                    using (document)
                    {
                        //The end is past the closing brace, so that a caller advancing the buffer by it
                        //leaves no stray brace behind to unbalance the next thing it reads.
                        end = i + 1;

                        string arguments;
                        if (TryFindArgumentsObject(document.RootElement, toolName, out arguments))
                        {
                            return arguments;
                        }

                        return CompactJson.Dump(document.RootElement);
                    }
                }

                if (braces < 0)
                {
                    braces = 0;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parses text as a JSON object, returning <see langword="null"/> rather than throwing when it is not
    /// one.
    /// </summary>
    /// <param name="text">The candidate JSON text.</param>
    /// <returns>The document, or <see langword="null"/>.</returns>
    private static JsonDocument TryParseObject(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            return null;
        }

        return document;
    }

    /// <summary>
    /// Looks through an object for the member that holds a call's arguments.
    /// </summary>
    /// <param name="element">The object to search.</param>
    /// <param name="toolName">The name of the tool whose arguments are wanted.</param>
    /// <param name="arguments">Receives the arguments as compact JSON text, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when this object is a tool call, even when it turned out to carry no usable
    /// arguments, in which case <paramref name="arguments"/> is <see langword="null"/>.
    /// </returns>
    private static bool TryFindArgumentsObject(JsonElement element, string toolName, out string arguments)
    {
        arguments = null;

        if (element.TryGetProperty("name", out JsonElement _))
        {
            if (TryReadArgumentsMember(element, "arguments", out arguments))
            {
                return true;
            }

            if (TryReadArgumentsMember(element, "parameters", out arguments))
            {
                return true;
            }

            arguments = null;
            return true;
        }

        if (!string.IsNullOrEmpty(toolName) && TryReadArgumentsMember(element, toolName, out arguments))
        {
            return true;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                if (TryFindArgumentsObject(property.Value, toolName, out arguments))
                {
                    return true;
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && TryFindArgumentsObject(item, toolName, out arguments))
                    {
                        return true;
                    }
                }
            }
        }

        arguments = null;
        return false;
    }

    /// <summary>
    /// Reads one member as an arguments object, accepting either a real object or a JSON object written as
    /// a string, which several models do.
    /// </summary>
    /// <param name="element">The object holding the member.</param>
    /// <param name="memberName">The member to read.</param>
    /// <param name="arguments">Receives the arguments as compact JSON text.</param>
    /// <returns><see langword="true"/> when the member held an arguments object.</returns>
    private static bool TryReadArgumentsMember(JsonElement element, string memberName, out string arguments)
    {
        arguments = null;

        JsonElement member;
        if (!element.TryGetProperty(memberName, out member))
        {
            return false;
        }

        if (member.ValueKind == JsonValueKind.Object)
        {
            arguments = CompactJson.Dump(member);
            return true;
        }

        if (member.ValueKind == JsonValueKind.String)
        {
            JsonDocument nested = TryParseObject(member.GetString() ?? string.Empty);
            if (nested != null)
            {
                using (nested)
                {
                    arguments = CompactJson.Dump(nested.RootElement);
                    return true;
                }
            }
        }

        return false;
    }
}

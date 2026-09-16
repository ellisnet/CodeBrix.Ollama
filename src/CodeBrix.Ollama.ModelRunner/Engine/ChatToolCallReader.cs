using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Reads tool calls out of a model's output, choosing between the two conventions models use to write one.
/// </summary>
/// <remarks>
/// Most models write a JSON object after a literal their template taught them, which
/// <see cref="ToolCallParser"/> reads. A few - Qwen 3.5 among them, which is the model this library is
/// built for - write nested XML tags instead, which <see cref="ChatFunctionCallParser"/> reads. The
/// template says which: a template that writes <c>&lt;function=</c> and <c>&lt;parameter=</c> literals
/// teaches the tag syntax, and anything else teaches the JSON one. This type hides the choice behind one
/// shape so the chat pipeline does not have to know.
/// </remarks>
internal sealed class ChatToolCallReader
{
    private readonly ToolCallParser json;
    private readonly ChatFunctionCallParser tagged;

    private ChatToolCallReader(ToolCallParser json, ChatFunctionCallParser tagged)
    {
        this.json = json;
        this.tagged = tagged;
    }

    /// <summary>The number of calls read so far.</summary>
    public int CallCount => json != null ? json.CallCount : tagged.CallCount;

    /// <summary>Whether the reader is reading the nested-tag convention rather than the JSON one.</summary>
    public bool UsesFunctionSyntax => tagged != null;

    /// <summary>Creates the reader a template's convention calls for.</summary>
    /// <param name="templateText">The chat template's source, which says which convention the model learnt.</param>
    /// <param name="format">The literals the model writes around a call.</param>
    /// <param name="offeredTools">The tools that were offered. Only these names become calls.</param>
    /// <returns>The reader.</returns>
    public static ChatToolCallReader Create(
        string templateText, ToolCallFormat format, IReadOnlyList<ToolDefinition> offeredTools)
    {
        if (ChatFunctionCallParser.TemplateUsesFunctionSyntax(templateText))
        {
            return new ChatToolCallReader(null, new ChatFunctionCallParser(format, offeredTools));
        }

        return new ChatToolCallReader(new ToolCallParser(format, offeredTools), null);
    }

    /// <summary>Takes the next chunk of output and returns the calls it completed and the content to emit.</summary>
    /// <param name="chunk">The next piece of the model's output. <see langword="null"/> is treated as empty.</param>
    /// <returns>The completed calls - empty when there are none - and the content text to emit now.</returns>
    public (IReadOnlyList<ToolCall> calls, string content) AddContent(string chunk)
    {
        if (tagged != null) return tagged.AddContent(chunk);

        return json.AddContent(chunk);
    }

    /// <summary>Ends the stream and returns whatever text was held back and never became a call.</summary>
    /// <returns>The remaining content text, usually empty.</returns>
    public string Flush()
    {
        if (tagged != null) return tagged.Flush();

        return json.Flush();
    }
}

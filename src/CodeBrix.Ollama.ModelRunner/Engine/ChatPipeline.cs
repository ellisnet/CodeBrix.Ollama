using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Turns the raw text a model generates for a chat request into the three things the contract promises:
/// reasoning, answer, and tool calls.
/// </summary>
/// <remarks>
/// <para>
/// The order is the one Ollama uses and it is not interchangeable. The reasoning block is separated first,
/// because a thinking model writes its whole plan - tool-call literals and all - inside the tags, and a
/// tool parser reading that plan would call a tool the model was only thinking about. What comes out as
/// answer text then goes through the tool reader, which holds back anything that might be the start of a
/// call and hands back the rest.
/// </para>
/// <para>
/// Both stages are optional. There is no reasoning stage for a model whose template has no thinking tags,
/// nor for a request that asked for thinking to be off - with two exceptions. One is a rendered prompt that
/// ends with the opening tag, in which case the model is already reasoning whatever the request asked for.
/// The other is a template that writes the tags but has no <c>enable_thinking</c> switch to turn them off
/// with: the request's "no" reached nothing, the model will reason anyway, and a pipeline that was not
/// watching for the tags would hand the reasoning back as the answer. There is no tool stage unless the
/// request offered tools, because a tool call is only a tool call when the tool was on offer.
/// </para>
/// <para>
/// Identifiers are assigned here. Neither convention for writing a call carries one, and the contract asks
/// a caller to echo one back on the tool result, so the pipeline numbers the calls of a turn
/// <c>call_1</c> upwards in the order the model wrote them.
/// </para>
/// <para>
/// Everything here is pure string work: one instance handles one response and it never touches the engine,
/// which is what lets the whole post-processing path be tested over a scripted token stream.
/// </para>
/// </remarks>
internal sealed class ChatPipeline
{
    private readonly ThinkingParser thinking;
    private readonly ChatToolCallReader tools;
    private readonly List<ToolCall> calls = new List<ToolCall>();

    /// <summary>Creates a pipeline from its two optional stages.</summary>
    /// <param name="thinkingParser">The reasoning splitter, or <see langword="null"/> when there is none.</param>
    /// <param name="toolCallReader">The tool-call reader, or <see langword="null"/> when there is none.</param>
    public ChatPipeline(ThinkingParser thinkingParser, ChatToolCallReader toolCallReader)
    {
        thinking = thinkingParser;
        tools = toolCallReader;
    }

    /// <summary>Every tool call read so far, in the order the model wrote them.</summary>
    public IReadOnlyList<ToolCall> ToolCalls => calls;

    /// <summary>Whether the pipeline is separating reasoning out of the stream.</summary>
    public bool ParsesThinking => thinking != null;

    /// <summary>Whether the pipeline is looking for tool calls in the stream.</summary>
    public bool ParsesToolCalls => tools != null;

    /// <summary>Builds the pipeline a request needs, from the template that rendered its prompt.</summary>
    /// <param name="request">The chat request.</param>
    /// <param name="dialect">The dialect the prompt was rendered in.</param>
    /// <param name="templateText">The template's source, which the tags and the literal are inferred from.</param>
    /// <param name="renderedPrompt">The prompt as rendered, which says whether the model starts inside a reasoning block.</param>
    /// <returns>The pipeline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public static ChatPipeline Create(
        ChatRequest request, ChatTemplateDialect dialect, string templateText, string renderedPrompt)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        string opening;
        string closing;

        bool hasTags = dialect == ChatTemplateDialect.Ollama
            ? ThinkingTags.InferGoTemplateTags(templateText, out opening, out closing)
            : ThinkingTags.InferJinjaTemplateTags(templateText, out opening, out closing);

        bool startsInside = hasTags && ThinkingTags.PromptEndsWithOpeningTag(renderedPrompt, opening);

        //A request that turned thinking off gets no reasoning stage, because the template has already put
        //an empty, closed reasoning block in the prompt and everything the model writes is the answer. That
        //holds only where the template could act on the request: one that ignored the switch and left the
        //block open says so in the prompt, and one that has no switch at all never saw the request's answer.
        bool switchable = HasThinkingSwitch(templateText);
        bool parseThinking = hasTags && (request.Think != false || startsInside || !switchable);

        ThinkingParser thinkingParser = parseThinking
            ? new ThinkingParser(opening, closing, startsInside)
            : null;

        ChatToolCallReader reader = null;
        if (request.Tools.Count > 0)
        {
            ToolCallFormat format = dialect == ChatTemplateDialect.Ollama
                ? ToolCallFormat.FromGoTemplateText(templateText)
                : ToolCallFormat.FromJinjaTemplateText(templateText);

            List<ToolDefinition> offered = new List<ToolDefinition>(request.Tools);
            reader = ChatToolCallReader.Create(templateText, format, offered);
        }

        return new ChatPipeline(thinkingParser, reader);
    }

    /// <summary>Takes the next chunk of the model's output.</summary>
    /// <param name="chunk">The text. <see langword="null"/> is treated as empty.</param>
    /// <returns>What the chunk separated out.</returns>
    public ChatPipelineOutput Add(string chunk)
    {
        string text = chunk ?? string.Empty;
        string reasoning = string.Empty;

        if (thinking != null)
        {
            (string thought, string answer) = thinking.AddContent(text);
            reasoning = thought;
            text = answer;
        }

        if (tools == null) return new ChatPipelineOutput(reasoning, text, null);

        (IReadOnlyList<ToolCall> found, string content) = tools.AddContent(text);
        return new ChatPipelineOutput(reasoning, content, Record(found));
    }

    /// <summary>Ends the response and returns whatever was still held back.</summary>
    /// <returns>What was left, which is usually empty.</returns>
    public ChatPipelineOutput Flush()
    {
        string reasoning = string.Empty;
        string text = string.Empty;

        if (thinking != null)
        {
            string remainder = thinking.Flush();
            if (thinking.State == ThinkingParserState.Thinking) reasoning = remainder;
            else text = remainder;
        }

        if (tools == null) return new ChatPipelineOutput(reasoning, text, null);

        (IReadOnlyList<ToolCall> found, string content) = tools.AddContent(text);
        return new ChatPipelineOutput(reasoning, content + tools.Flush(), Record(found));
    }

    /// <summary>
    /// Whether a template has a switch a request's <see cref="ChatRequest.Think"/> can act on at all.
    /// </summary>
    /// <param name="templateText">The template's source. <see langword="null"/> is treated as empty.</param>
    /// <returns><see langword="true"/> when the template reads the thinking switch.</returns>
    /// <remarks>
    /// <c>enable_thinking</c> is the name every template that takes the switch uses, and it is the name this
    /// engine renders the request's answer under, so its presence in the source is the whole test. Ollama's
    /// own dialect has no switch of its own; a Go template that names it reads it out of the values.
    /// </remarks>
    private static bool HasThinkingSwitch(string templateText)
    {
        string template = templateText ?? string.Empty;

        return template.Contains("enable_thinking", StringComparison.Ordinal);
    }

    /// <summary>Numbers a step's calls and keeps them.</summary>
    /// <param name="found">The calls the reader produced.</param>
    /// <returns>The same calls with their identifiers set.</returns>
    private IReadOnlyList<ToolCall> Record(IReadOnlyList<ToolCall> found)
    {
        if (found == null || found.Count == 0) return null;

        List<ToolCall> numbered = new List<ToolCall>(found.Count);

        foreach (ToolCall call in found)
        {
            if (call == null) continue;

            ToolCall identified = new ToolCall
            {
                Id = "call_" + (calls.Count + 1).ToString(CultureInfo.InvariantCulture),
                Name = call.Name,
                ArgumentsJson = call.ArgumentsJson,
            };

            calls.Add(identified);
            numbered.Add(identified);
        }

        return numbered;
    }
}

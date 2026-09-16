using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Builds the values an Ollama Go chat template renders from, out of a <see cref="ChatRequest"/>.
/// </summary>
/// <remarks>
/// There is far less to do here than for a Jinja template, because
/// <see cref="OllamaTemplateValues"/> already speaks this library's own contract types: the template engine
/// binds <see cref="ChatMessage"/> and <see cref="ToolDefinition"/> to the fields Ollama's templates read,
/// collates consecutive messages of one role and lifts the system messages out, all by itself. The system
/// prompt therefore stays where the caller put it, in the message list, rather than being moved into
/// <see cref="OllamaTemplateValues.System"/>: Ollama's own collation offers it as <c>.System</c> either
/// way, and moving it would change the order a template that ranges over <c>.Messages</c> sees.
/// </remarks>
internal static class ChatOllamaValues
{
    /// <summary>Builds the values for one request.</summary>
    /// <param name="request">The chat request.</param>
    /// <returns>The values, ready for <see cref="OllamaTemplate.Render"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public static OllamaTemplateValues Build(ChatRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        List<ChatMessage> messages = new List<ChatMessage>(request.Messages.Count);
        foreach (ChatMessage message in request.Messages)
        {
            if (message != null) messages.Add(message);
        }

        List<ToolDefinition> tools = new List<ToolDefinition>(request.Tools.Count);
        foreach (ToolDefinition tool in request.Tools)
        {
            if (tool != null) tools.Add(tool);
        }

        return new OllamaTemplateValues
        {
            Messages = messages,
            Tools = tools,
            Think = request.Think,
        };
    }
}

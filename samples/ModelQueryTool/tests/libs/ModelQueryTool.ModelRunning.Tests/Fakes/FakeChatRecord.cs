using System.Collections.Generic;
using CodeBrix.Ollama.ModelRunner;

namespace ModelQueryTool.ModelRunning.Tests.Fakes;

/// <summary>
/// A copy of one request the fake model was given, taken when it arrived so that a later turn cannot change
/// what a test reads.
/// </summary>
public sealed class FakeChatRecord
{
    /// <summary>Copies the request.</summary>
    /// <param name="request">The request as it arrived.</param>
    public FakeChatRecord(ChatRequest request)
    {
        List<ChatMessage> messages = new List<ChatMessage>();

        foreach (ChatMessage message in request.Messages)
        {
            messages.Add(new ChatMessage(message.Role, message.Content) { Thinking = message.Thinking });
        }

        Messages = messages;
        Think = request.Think;
    }

    /// <summary>Gets the messages the request carried, oldest first.</summary>
    public IReadOnlyList<ChatMessage> Messages { get; }

    /// <summary>Gets what the request asked about reasoning.</summary>
    public bool? Think { get; }
}

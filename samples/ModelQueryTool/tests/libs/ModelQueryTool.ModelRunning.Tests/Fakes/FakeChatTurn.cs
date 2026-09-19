using System;
using System.Collections.Generic;
using CodeBrix.Ollama.ModelRunner;

namespace ModelQueryTool.ModelRunning.Tests.Fakes;

/// <summary>
/// One scripted reply: the pieces it streams, how it ends, and - where a test wants it - how it goes wrong.
/// </summary>
public sealed class FakeChatTurn
{
    /// <summary>Gets the pieces this reply streams, in order.</summary>
    public IList<FakeChatDelta> Deltas { get; } = new List<FakeChatDelta>();

    /// <summary>Gets or sets why the reply ended.</summary>
    public FinishReason FinishReason { get; set; } = FinishReason.Stop;

    /// <summary>
    /// Gets or sets what the reply reports it cost, or null to have the fake work it out from the request
    /// and the reply.
    /// </summary>
    public GenerationStatistics Statistics { get; set; }

    /// <summary>Gets or sets an exception thrown once the pieces have been streamed, or null for none.</summary>
    public Exception Failure { get; set; }

    /// <summary>
    /// Gets or sets whether the reply waits, once the pieces have been streamed, until its token is
    /// cancelled.
    /// </summary>
    public bool BlocksUntilCancelled { get; set; }

    /// <summary>Makes a reply that streams one piece of answer text and stops.</summary>
    /// <param name="content">The answer text.</param>
    /// <returns>The reply.</returns>
    public static FakeChatTurn Answering(string content)
    {
        return new FakeChatTurn().Saying(content);
    }

    /// <summary>Adds a piece of reasoning to the reply.</summary>
    /// <param name="text">The reasoning.</param>
    /// <returns>The same reply, for chaining.</returns>
    public FakeChatTurn Thinking(string text)
    {
        Deltas.Add(FakeChatDelta.Thinking(text));

        return this;
    }

    /// <summary>Adds a piece of answer text to the reply.</summary>
    /// <param name="text">The answer text.</param>
    /// <returns>The same reply, for chaining.</returns>
    public FakeChatTurn Saying(string text)
    {
        Deltas.Add(FakeChatDelta.Content(text));

        return this;
    }

    /// <summary>Makes the reply end for the given reason.</summary>
    /// <param name="reason">Why it ended.</param>
    /// <returns>The same reply, for chaining.</returns>
    public FakeChatTurn EndingWith(FinishReason reason)
    {
        FinishReason = reason;

        return this;
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;

namespace ModelQueryTool.ModelRunning.Tests.Fakes;

/// <summary>
/// A model that hands out scripted replies instead of running anything, so the host can be tested without a
/// model file. It records every request it is given, and it counts one token per whitespace-separated word
/// of the rendered prompt, which is what makes the context arithmetic exact in a test.
/// </summary>
public sealed class FakeRunningModel : IRunningModel
{
    private static readonly char[] Whitespace = { ' ', '\t', '\r', '\n' };

    private readonly Queue<FakeChatTurn> _turns = new Queue<FakeChatTurn>();

    /// <summary>Gets or sets what the fake says about itself.</summary>
    public ModelDetails Details { get; set; } = new ModelDetails
    {
        Path = "fake-model-q4_k_m.gguf",
        Architecture = "qwen3next",
        Name = "fake",
        TrainingContextLength = 262144,
        VocabularySize = 128,
        EmbeddingLength = 8,
        LayerCount = 2,
        HeadCount = 2,
        HasDecoder = true,
        Metadata = new Dictionary<string, string>(),
    };

    /// <summary>Gets or sets the options the fake pretends it was loaded with.</summary>
    public ModelRunnerOptions Options { get; set; } = new ModelRunnerOptions { ModelPath = "fake-model-q4_k_m.gguf" };

    /// <summary>Gets or sets the chat-template dialect the fake reports.</summary>
    public ChatTemplateDialect ChatTemplateDialect { get; set; } = ChatTemplateDialect.Jinja;

    /// <summary>Gets the requests the fake was asked to answer, in order.</summary>
    public IList<FakeChatRecord> ChatRequests { get; } = new List<FakeChatRecord>();

    /// <summary>Gets the requests the fake was asked to render, in order.</summary>
    public IList<FakeChatRecord> RenderedRequests { get; } = new List<FakeChatRecord>();

    /// <summary>Gets whether the fake has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Gets or sets the reply used once the scripted ones have run out.</summary>
    public FakeChatTurn DefaultTurn { get; set; } = FakeChatTurn.Answering("ok");

    /// <summary>Adds a reply to the script.</summary>
    /// <param name="turn">The reply.</param>
    /// <returns>The same fake, for chaining.</returns>
    public FakeRunningModel Script(FakeChatTurn turn)
    {
        _turns.Enqueue(turn);

        return this;
    }

    /// <summary>Renders a request the way this fake's template would: one marker word per message.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The prompt text.</returns>
    public static string Render(ChatRequest request)
    {
        StringBuilder prompt = new StringBuilder();

        foreach (ChatMessage message in request.Messages)
        {
            prompt.Append('<').Append(message.Role.ToString().ToLowerInvariant()).Append("> ");

            if (!string.IsNullOrEmpty(message.Content))
            {
                prompt.Append(message.Content).Append(' ');
            }
        }

        prompt.Append("<assistant>");

        return prompt.ToString();
    }

    /// <summary>Counts one token per whitespace-separated word.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The token count.</returns>
    public static int CountTokens(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : text.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<int>> TokenizeAsync(
        string text,
        bool addSpecialTokens = true,
        bool parseSpecialTokens = true,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        int count = CountTokens(text);
        List<int> tokens = new List<int>(count);

        for (int index = 0; index < count; index++)
        {
            tokens.Add(index + 1);
        }

        return Task.FromResult<IReadOnlyList<int>>(tokens);
    }

    /// <inheritdoc />
    public Task<string> DetokenizeAsync(
        IReadOnlyList<int> tokens, bool renderSpecialTokens = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        return Task.FromResult(string.Empty);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<GenerationUpdate> GenerateAsync(
        string prompt, GenerationOptions options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("The host holds conversations, so the fake only answers chat requests.");
    }

    /// <inheritdoc />
    public Task<GenerationResult> GenerateToEndAsync(
        string prompt, GenerationOptions options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("The host holds conversations, so the fake only answers chat requests.");
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatUpdate> ChatAsync(
        ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ChatRequests.Add(new FakeChatRecord(request));
        FakeChatTurn turn = _turns.Count > 0 ? _turns.Dequeue() : DefaultTurn;
        StringBuilder content = new StringBuilder();

        foreach (FakeChatDelta delta in turn.Deltas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            content.Append(delta.ContentDelta);

            yield return new ChatUpdate
            {
                ThinkingDelta = delta.ThinkingDelta,
                ContentDelta = delta.ContentDelta,
            };
        }

        if (turn.BlocksUntilCancelled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        if (turn.Failure != null)
        {
            throw turn.Failure;
        }

        yield return new ChatUpdate
        {
            IsFinal = true,
            FinishReason = turn.FinishReason,
            Statistics = turn.Statistics ?? new GenerationStatistics
            {
                PromptTokens = CountTokens(Render(request)),
                GeneratedTokens = CountTokens(content.ToString()),
                GenerationDuration = TimeSpan.FromSeconds(1),
                TotalDuration = TimeSpan.FromSeconds(1),
            },
        };
    }

    /// <inheritdoc />
    public Task<ChatResponse> ChatToEndAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("The host streams every turn, so the fake never collects one.");
    }

    /// <inheritdoc />
    public Task<string> RenderChatPromptAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        RenderedRequests.Add(new FakeChatRecord(request));

        return Task.FromResult(Render(request));
    }

    /// <inheritdoc />
    public Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("The host does not embed anything.");
    }

    /// <inheritdoc />
    public Task SetLoraAdaptersAsync(
        IReadOnlyList<LoraAdapterOptions> adapters, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("The host applies no adapters.");
    }

    /// <inheritdoc />
    public Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        IsDisposed = true;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;

        return ValueTask.CompletedTask;
    }
}

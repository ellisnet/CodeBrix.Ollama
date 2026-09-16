using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// An <see cref="IRunningModel"/> that returns scripted replies instead of running a model, standing in for
/// a real one in an application's own tests.
/// </summary>
/// <remarks>
/// <para>
/// It is here to prove something about the contract rather than about the engine: every member of
/// <see cref="IRunningModel"/> can be implemented by ordinary application code with no access to the engine
/// at all, which is what lets an application test the code around a model without loading one. If a future
/// change to the interface made that impossible, this type would stop compiling.
/// </para>
/// <para>
/// A reply is streamed one word at a time so that a consumer of the streaming members really sees several
/// updates.
/// </para>
/// </remarks>
public sealed class FakeRunningModel : IRunningModel
{
    private readonly Queue<string> replies;

    /// <summary>Creates the fake with the replies it hands out, in order.</summary>
    /// <param name="scriptedReplies">The replies. The last one is repeated once they run out.</param>
    public FakeRunningModel(params string[] scriptedReplies)
    {
        replies = new Queue<string>(scriptedReplies ?? Array.Empty<string>());
        LastReply = string.Empty;
    }

    /// <summary>What the fake says about itself.</summary>
    public ModelDetails Details { get; set; } = new ModelDetails
    {
        Path = "fake.gguf",
        Architecture = "llama",
        Name = "fake",
        VocabularySize = 32,
        EmbeddingLength = 4,
        LayerCount = 1,
        HeadCount = 1,
        HasDecoder = true,
        Metadata = new Dictionary<string, string>(),
    };

    /// <summary>The options the fake pretends it was loaded with.</summary>
    public ModelRunnerOptions Options { get; set; } = new ModelRunnerOptions { ModelPath = "fake.gguf" };

    /// <summary>The dialect the fake reports.</summary>
    public ChatTemplateDialect ChatTemplateDialect { get; set; } = ChatTemplateDialect.Native;

    /// <summary>The reply the fake last handed out.</summary>
    public string LastReply { get; private set; }

    /// <summary>How many times the cache was cleared.</summary>
    public int ClearCacheCalls { get; private set; }

    /// <summary>The adapters the fake was last given.</summary>
    public IReadOnlyList<LoraAdapterOptions> Adapters { get; private set; } = Array.Empty<LoraAdapterOptions>();

    /// <summary>Whether the fake has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Splits the text into one token id per character.</summary>
    /// <param name="text">The text.</param>
    /// <param name="addSpecialTokens">Ignored.</param>
    /// <param name="parseSpecialTokens">Ignored.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The token ids.</returns>
    public Task<IReadOnlyList<int>> TokenizeAsync(
        string text,
        bool addSpecialTokens = true,
        bool parseSpecialTokens = true,
        CancellationToken cancellationToken = default)
    {
        List<int> tokens = new List<int>(text.Length);
        foreach (char character in text) tokens.Add(character);
        return Task.FromResult<IReadOnlyList<int>>(tokens);
    }

    /// <summary>Turns the character token ids back into text.</summary>
    /// <param name="tokens">The token ids.</param>
    /// <param name="renderSpecialTokens">Ignored.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The text.</returns>
    public Task<string> DetokenizeAsync(
        IReadOnlyList<int> tokens, bool renderSpecialTokens = false, CancellationToken cancellationToken = default)
    {
        StringBuilder text = new StringBuilder();
        foreach (int token in tokens) text.Append((char)token);
        return Task.FromResult(text.ToString());
    }

    /// <summary>Streams the next scripted reply, one word at a time.</summary>
    /// <param name="prompt">Ignored.</param>
    /// <param name="options">Only <see cref="GenerationOptions.MaxTokens"/> is honoured.</param>
    /// <param name="cancellationToken">A token to cancel the stream.</param>
    /// <returns>The updates.</returns>
    public async IAsyncEnumerable<GenerationUpdate> GenerateAsync(
        string prompt,
        GenerationOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string reply = NextReply();
        string[] words = reply.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int emitted = 0;
        foreach (string word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options != null && options.MaxTokens.HasValue && emitted >= options.MaxTokens.Value) break;

            emitted++;
            await Task.Yield();
            yield return new GenerationUpdate { Text = emitted == 1 ? word : " " + word, Tokens = new[] { emitted } };
        }

        yield return new GenerationUpdate
        {
            IsFinal = true,
            FinishReason = FinishReason.Stop,
            Statistics = new GenerationStatistics
            {
                PromptTokens = prompt == null ? 0 : prompt.Length,
                GeneratedTokens = emitted,
                GenerationDuration = TimeSpan.FromMilliseconds(emitted),
                TotalDuration = TimeSpan.FromMilliseconds(emitted + 1),
            },
        };
    }

    /// <summary>Returns the next scripted reply whole.</summary>
    /// <param name="prompt">Ignored.</param>
    /// <param name="options">Only <see cref="GenerationOptions.MaxTokens"/> is honoured.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    public async Task<GenerationResult> GenerateToEndAsync(
        string prompt, GenerationOptions options = null, CancellationToken cancellationToken = default)
    {
        StringBuilder text = new StringBuilder();
        GenerationStatistics statistics = null;
        FinishReason reason = FinishReason.None;

        await foreach (GenerationUpdate update in GenerateAsync(prompt, options, cancellationToken))
        {
            text.Append(update.Text);
            if (!update.IsFinal) continue;

            reason = update.FinishReason;
            statistics = update.Statistics;
        }

        return new GenerationResult { Text = text.ToString(), FinishReason = reason, Statistics = statistics };
    }

    /// <summary>Streams the next scripted reply as a chat reply.</summary>
    /// <param name="request">The request; only its options are looked at.</param>
    /// <param name="cancellationToken">A token to cancel the stream.</param>
    /// <returns>The updates.</returns>
    public async IAsyncEnumerable<ChatUpdate> ChatAsync(
        ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string prompt = await RenderChatPromptAsync(request, cancellationToken);
        GenerationStatistics statistics = null;

        await foreach (GenerationUpdate update in GenerateAsync(prompt, request.Options, cancellationToken))
        {
            if (!update.IsFinal)
            {
                yield return new ChatUpdate { ContentDelta = update.Text };
                continue;
            }

            statistics = update.Statistics;
        }

        yield return new ChatUpdate
        {
            IsFinal = true,
            FinishReason = FinishReason.Stop,
            Statistics = statistics,
        };
    }

    /// <summary>Returns the next scripted reply as a whole chat reply.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The reply.</returns>
    public async Task<ChatResponse> ChatToEndAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        StringBuilder content = new StringBuilder();
        GenerationStatistics statistics = null;
        FinishReason reason = FinishReason.None;

        await foreach (ChatUpdate update in ChatAsync(request, cancellationToken))
        {
            content.Append(update.ContentDelta);
            if (!update.IsFinal) continue;

            reason = update.FinishReason;
            statistics = update.Statistics;
        }

        return new ChatResponse
        {
            Message = new ChatMessage(ChatRole.Assistant, content.ToString()),
            FinishReason = reason,
            Statistics = statistics,
        };
    }

    /// <summary>Renders the conversation as one plain line per message.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The prompt.</returns>
    public Task<string> RenderChatPromptAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        StringBuilder prompt = new StringBuilder();
        foreach (ChatMessage message in request.Messages)
        {
            prompt.Append(message.Role).Append(": ").Append(message.Content).Append('\n');
        }

        return Task.FromResult(prompt.ToString());
    }

    /// <summary>Returns a fixed-length vector per input, made from the input's own characters.</summary>
    /// <param name="inputs">The texts.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The vectors.</returns>
    public Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        int dimensions = Details.EmbeddingLength;
        List<float[]> vectors = new List<float[]>(inputs.Count);
        int tokens = 0;

        foreach (string input in inputs)
        {
            tokens += input.Length;
            float[] vector = new float[dimensions];
            for (int i = 0; i < input.Length; i++) vector[i % dimensions] += input[i];
            vectors.Add(vector);
        }

        return Task.FromResult(new EmbeddingResult
        {
            Embeddings = vectors,
            Dimensions = dimensions,
            PromptTokens = tokens,
        });
    }

    /// <summary>Records the adapters it was given.</summary>
    /// <param name="adapters">The adapters.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A completed task.</returns>
    public Task SetLoraAdaptersAsync(
        IReadOnlyList<LoraAdapterOptions> adapters, CancellationToken cancellationToken = default)
    {
        Adapters = new List<LoraAdapterOptions>(adapters);
        return Task.CompletedTask;
    }

    /// <summary>Counts the call.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A completed task.</returns>
    public Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        ClearCacheCalls++;
        return Task.CompletedTask;
    }

    /// <summary>Marks the fake disposed.</summary>
    public void Dispose()
    {
        IsDisposed = true;
    }

    /// <summary>Marks the fake disposed.</summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    private string NextReply()
    {
        if (replies.Count > 0) LastReply = replies.Dequeue();
        return LastReply;
    }
}

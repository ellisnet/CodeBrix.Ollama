using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A model that is loaded and ready: the contract an application holds to query it. Obtained from
/// <see cref="ModelRunner.LoadAsync"/>; disposing it unloads the model.
/// </summary>
/// <remarks>
/// <para>
/// Every request is independent: a chat request carries the whole conversation, and a completion request
/// carries the whole prompt. The implementation reuses whatever prefix of the previous request is still in
/// its cache, so a growing conversation does not re-evaluate its history each turn. Requests on one instance
/// are executed one at a time; concurrent callers wait their turn.
/// </para>
/// <para>
/// A streaming request holds its turn until its enumeration completes or is disposed. Do not call another
/// member of the same instance from inside an <c>await foreach</c> over <see cref="GenerateAsync"/> or
/// <see cref="ChatAsync"/>: the implementation detects that and throws <see cref="InvalidOperationException"/>
/// rather than wait on itself. Finish or dispose the enumeration first.
/// </para>
/// <para>
/// Application code and tests can implement this interface themselves to stand in for a real model.
/// </para>
/// </remarks>
public interface IRunningModel : IDisposable, IAsyncDisposable
{
    /// <summary>What the engine knows about the loaded model.</summary>
    ModelDetails Details { get; }

    /// <summary>The options the model was loaded with.</summary>
    ModelRunnerOptions Options { get; }

    /// <summary>The chat-template dialect that will render chat requests, resolved from the options and the model.</summary>
    ChatTemplateDialect ChatTemplateDialect { get; }

    /// <summary>Converts text to token ids.</summary>
    /// <param name="text">The text.</param>
    /// <param name="addSpecialTokens">Whether to add the model's beginning-of-sequence token where it expects one. Default true.</param>
    /// <param name="parseSpecialTokens">Whether special-token text in the input (for example a chat marker) is recognized as the token. Default true.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The token ids.</returns>
    Task<IReadOnlyList<int>> TokenizeAsync(string text, bool addSpecialTokens = true, bool parseSpecialTokens = true, CancellationToken cancellationToken = default);

    /// <summary>Converts token ids back to text.</summary>
    /// <param name="tokens">The token ids.</param>
    /// <param name="renderSpecialTokens">Whether special tokens are rendered as their text rather than dropped. Default false.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The text.</returns>
    Task<string> DetokenizeAsync(IReadOnlyList<int> tokens, bool renderSpecialTokens = false, CancellationToken cancellationToken = default);

    /// <summary>Streams a completion of a raw prompt. No chat template is applied.</summary>
    /// <param name="prompt">The prompt text.</param>
    /// <param name="options">The generation controls, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Updates as text is produced; the last one has <see cref="GenerationUpdate.IsFinal"/> set.</returns>
    IAsyncEnumerable<GenerationUpdate> GenerateAsync(string prompt, GenerationOptions options = null, CancellationToken cancellationToken = default);

    /// <summary>Completes a raw prompt and returns the whole result. No chat template is applied.</summary>
    /// <param name="prompt">The prompt text.</param>
    /// <param name="options">The generation controls, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The completed result.</returns>
    Task<GenerationResult> GenerateToEndAsync(string prompt, GenerationOptions options = null, CancellationToken cancellationToken = default);

    /// <summary>Streams the reply to a chat request, rendering the conversation through the model's chat template.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Updates as the reply is produced; the last one has <see cref="ChatUpdate.IsFinal"/> set and carries any tool calls.</returns>
    IAsyncEnumerable<ChatUpdate> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>Replies to a chat request and returns the whole reply.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The completed reply.</returns>
    Task<ChatResponse> ChatToEndAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>Renders a chat request through the chat template and returns the prompt text, without generating anything.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The prompt the model would see.</returns>
    Task<string> RenderChatPromptAsync(ChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>Computes embeddings for one or more inputs.</summary>
    /// <param name="inputs">The texts.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>One vector per input.</returns>
    Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the set of LoRA adapters applied to the model. An empty list removes them all. Adapters are
    /// loaded on first use by path and kept until the model is disposed.
    /// </summary>
    /// <param name="adapters">The adapters and their scales.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the adapters are applied.</returns>
    Task SetLoraAdaptersAsync(IReadOnlyList<LoraAdapterOptions> adapters, CancellationToken cancellationToken = default);

    /// <summary>Clears the context cache, so the next request evaluates its whole prompt.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the cache is cleared.</returns>
    Task ClearCacheAsync(CancellationToken cancellationToken = default);
}

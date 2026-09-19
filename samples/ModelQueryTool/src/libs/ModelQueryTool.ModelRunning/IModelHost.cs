using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;

namespace ModelQueryTool.ModelRunning;

/// <summary>
/// The loaded model and the conversation being held with it. The host is handed the path of a model file,
/// keeps the conversation as one growing list of messages so the model runner's prefix cache can make each
/// turn cheap, streams a turn's reasoning and answer as they arrive, and unloads the model again.
/// </summary>
/// <remarks>
/// Every member is safe to call from any thread. One turn runs at a time: a second message while a turn is
/// streaming is refused rather than queued. Disposing the host cancels a turn in flight and unloads the
/// model.
/// </remarks>
public interface IModelHost : IAsyncDisposable
{
    /// <summary>Gets where the host is in the model's life.</summary>
    ModelHostState State { get; }

    /// <summary>
    /// Occurs when <see cref="State"/> changes, carrying the new state. It is raised on whichever thread
    /// made the change, so an application with a user interface marshals it itself.
    /// </summary>
    event EventHandler<ModelHostState> StateChanged;

    /// <summary>Gets what the engine knows about the loaded model, or null while no model is loaded.</summary>
    ModelDetails Details { get; }

    /// <summary>Gets the path of the loaded model file, or null while no model is loaded.</summary>
    string ModelPath { get; }

    /// <summary>Gets the context size the model is loaded with, and that the next start would load it with.</summary>
    uint ContextSize { get; }

    /// <summary>Gets the context the model was trained for, or 0 while no model is loaded.</summary>
    int TrainedContextSize { get; }

    /// <summary>
    /// Gets or sets whether the model is asked to reason before it answers. Setting it takes effect on the
    /// next turn and clears nothing: the conversation and the loaded model are untouched.
    /// </summary>
    bool Think { get; set; }

    /// <summary>Gets the instruction that opens the conversation, or null when there is none.</summary>
    string SystemPrompt { get; }

    /// <summary>Gets how many turns the current conversation holds.</summary>
    int TurnCount { get; }

    /// <summary>
    /// Gets how many tokens the conversation occupied after the last turn - its prompt and what the model
    /// generated - or 0 when no turn has finished in this conversation.
    /// </summary>
    int ConversationTokens { get; }

    /// <summary>Loads the model at the given path and makes the host ready for a first message.</summary>
    /// <param name="modelPath">The model file to load.</param>
    /// <param name="loadProgress">Told how far the load has got, from 0.0 to 1.0, or null for no reporting.</param>
    /// <param name="cancellationToken">A token to abandon the load.</param>
    /// <returns>A task that completes when the model is loaded.</returns>
    /// <exception cref="ModelRunningException">The host is not stopped, the path is blank, or the load failed.</exception>
    /// <exception cref="ObjectDisposedException">The host has been disposed.</exception>
    Task StartAsync(string modelPath, IProgress<float> loadProgress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels any turn in flight, waits for it to unwind, and unloads the model. It does nothing when no
    /// model is loaded, and may be called as often as you like.
    /// </summary>
    /// <returns>A task that completes when the model is unloaded.</returns>
    /// <exception cref="ObjectDisposedException">The host has been disposed.</exception>
    Task StopAsync();

    /// <summary>
    /// Loads the model again at another context size and starts a new conversation. The context size is
    /// fixed when a model is loaded, so there is no cheaper way to change it. When the new size does not
    /// load, the previous size is loaded again so the user is never left without a model.
    /// </summary>
    /// <param name="contextSize">The context size to load at.</param>
    /// <param name="loadProgress">Told how far each load has got, from 0.0 to 1.0, or null for no reporting.</param>
    /// <param name="cancellationToken">A token to abandon the reload.</param>
    /// <returns>A task that completes when a model is loaded again.</returns>
    /// <exception cref="ModelRunningException">
    /// No model is loaded; the size is below <see cref="ModelHostOptions.MinimumContextSize"/> or above the
    /// model's trained context; or the new size did not load - in which case the message says so and says
    /// which size is running instead.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The host has been disposed.</exception>
    Task ReloadAsync(uint contextSize, IProgress<float> loadProgress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the instruction that opens the conversation, and starts a new conversation because the answers
    /// already given were written under other instructions. The model is NOT reloaded: a system prompt is
    /// only the first message of a request.
    /// </summary>
    /// <param name="text">The instruction, or null or empty to have none.</param>
    /// <exception cref="ModelRunningException">A turn is being streamed.</exception>
    /// <exception cref="ObjectDisposedException">The host has been disposed.</exception>
    void SetSystemPrompt(string text);

    /// <summary>Forgets the conversation, keeping the system prompt and the loaded model.</summary>
    /// <exception cref="ModelRunningException">A turn is being streamed.</exception>
    /// <exception cref="ObjectDisposedException">The host has been disposed.</exception>
    void ClearConversation();

    /// <summary>
    /// Sends a message and streams the reply. The whole conversation is sent, oldest first, so the prefix
    /// cache only has to evaluate what is new. When earlier turns had to be dropped to make room, the first
    /// piece of the stream is a <see cref="ChatTurnUpdateKind.Notice"/> saying so.
    /// </summary>
    /// <param name="userText">What the user wrote.</param>
    /// <param name="cancellationToken">A token to stop the turn.</param>
    /// <returns>The pieces of the turn, ending with a <see cref="ChatTurnUpdateKind.Completed"/> one.</returns>
    /// <exception cref="ModelRunningException">
    /// The host is not ready, the message is empty, or the message alone does not fit in the context.
    /// </exception>
    /// <exception cref="OperationCanceledException">The turn was cancelled; the conversation is unchanged.</exception>
    /// <exception cref="ObjectDisposedException">The host has been disposed.</exception>
    IAsyncEnumerable<ChatTurnUpdate> SendAsync(string userText, CancellationToken cancellationToken = default);
}

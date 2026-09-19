using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;
using ModelQueryTool.ModelRunning;

namespace ModelQueryTool.Core.Tests.Fakes;

/// <summary>
/// A model host with no model behind it: it hands back the pieces of a turn the test wrote,
/// counts what was asked of it, and can stop part way through a turn so the test can cancel one.
/// </summary>
internal sealed class FakeModelHost : IModelHost
{
    /// <summary>The value of <see cref="PauseBeforeUpdate"/> that means "never stop".</summary>
    internal const int NoPause = -1;

    private readonly List<ChatTurnUpdate> _updates = [];
    private readonly List<string> _sent = [];
    private readonly List<float> _loadReports = [];
    private readonly TaskCompletionSource _reachedPause = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public event EventHandler<ModelHostState> StateChanged;

    /// <inheritdoc />
    public ModelHostState State { get; private set; } = ModelHostState.Stopped;

    /// <inheritdoc />
    public ModelDetails Details { get; set; }

    /// <inheritdoc />
    public string ModelPath { get; private set; }

    /// <inheritdoc />
    public uint ContextSize { get; set; } = 8192;

    /// <inheritdoc />
    public int TrainedContextSize { get; set; }

    /// <inheritdoc />
    public bool Think { get; set; } = true;

    /// <inheritdoc />
    public string SystemPrompt { get; private set; }

    /// <inheritdoc />
    public int TurnCount { get; private set; }

    /// <inheritdoc />
    public int ConversationTokens { get; set; }

    /// <summary>Gets the pieces a turn is made of, in the order they are handed over.</summary>
    internal IList<ChatTurnUpdate> Updates => _updates;

    /// <summary>Gets everything that was sent to the model, in order.</summary>
    internal IReadOnlyList<string> Sent => _sent;

    /// <summary>Gets the fractions a load reports before it finishes.</summary>
    internal IList<float> LoadReports => _loadReports;

    /// <summary>
    /// Gets or sets the index of the piece the turn stops in front of, waiting to be released or
    /// cancelled. Zero stops before anything is written, which is the wait every real turn opens
    /// with.
    /// </summary>
    internal int PauseBeforeUpdate { get; set; } = NoPause;

    /// <summary>Gets a task that completes when the turn has reached its pause.</summary>
    internal Task ReachedPause => _reachedPause.Task;

    /// <summary>Gets or sets what a turn throws after its pieces have been handed over.</summary>
    internal Exception TurnFailure { get; set; }

    /// <summary>Gets or sets what <see cref="SendAsync"/> throws from the call itself.</summary>
    internal Exception SendFailure { get; set; }

    /// <summary>Gets or sets what a load throws instead of loading.</summary>
    internal Exception StartFailure { get; set; }

    /// <summary>Gets or sets what a reload throws instead of reloading.</summary>
    internal Exception ReloadFailure { get; set; }

    /// <summary>Gets or sets whether a load waits to be cancelled instead of finishing.</summary>
    internal bool StartWaitsToBeCancelled { get; set; }

    /// <summary>Gets or sets whether a load waits for <see cref="Release"/> before it finishes.</summary>
    internal bool StartWaitsForRelease { get; set; }

    /// <summary>Gets how many times a model was loaded.</summary>
    internal int StartCalls { get; private set; }

    /// <summary>Gets how many times the model was stopped.</summary>
    internal int StopCalls { get; private set; }

    /// <summary>Gets how many times the model was loaded again at another context size.</summary>
    internal int ReloadCalls { get; private set; }

    /// <summary>Gets the context sizes a reload was asked for, in order.</summary>
    internal IList<uint> ReloadSizes { get; } = [];

    /// <summary>Gets how many times the conversation was cleared.</summary>
    internal int ClearCalls { get; private set; }

    /// <summary>Gets how many times the system prompt was set.</summary>
    internal int SystemPromptCalls { get; private set; }

    /// <summary>Gets how many times the host was disposed.</summary>
    internal int DisposeCalls { get; private set; }

    /// <summary>Gets whether the fake has been disposed.</summary>
    internal bool IsDisposed { get; private set; }

    /// <summary>Lets a paused turn carry on.</summary>
    internal void Release() => _release.TrySetResult();

    /// <summary>
    /// Sets every "how many times" count back to zero, so that a test can arrange whatever it
    /// likes and then assert about the one call it is actually looking at.
    /// </summary>
    internal void ResetCounts()
    {
        StartCalls = 0;
        StopCalls = 0;
        ReloadCalls = 0;
        ClearCalls = 0;
        SystemPromptCalls = 0;
        DisposeCalls = 0;
    }

    /// <summary>Puts the fake into the state a loaded model leaves it in, without loading anything.</summary>
    /// <param name="modelPath">The path to report as loaded.</param>
    internal void PretendLoaded(string modelPath = "/tmp/fake-model-query-tool/models/blobs/sha256-weights")
    {
        ModelPath = modelPath;
        SetState(ModelHostState.Ready);
    }

    /// <inheritdoc />
    public async Task StartAsync(
        string modelPath, IProgress<float> loadProgress = null, CancellationToken cancellationToken = default)
    {
        StartCalls++;
        SetState(ModelHostState.Starting);

        foreach (var fraction in _loadReports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            loadProgress?.Report(fraction);
        }

        if (StartWaitsForRelease)
        {
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (StartWaitsToBeCancelled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        if (StartFailure != null)
        {
            SetState(ModelHostState.Stopped);
            throw StartFailure;
        }

        ModelPath = modelPath;
        SetState(ModelHostState.Ready);
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        StopCalls++;
        ModelPath = null;
        SetState(ModelHostState.Stopped);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReloadAsync(
        uint contextSize, IProgress<float> loadProgress = null, CancellationToken cancellationToken = default)
    {
        ReloadCalls++;
        ReloadSizes.Add(contextSize);

        foreach (var fraction in _loadReports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            loadProgress?.Report(fraction);
        }

        if (ReloadFailure != null) { throw ReloadFailure; }

        ContextSize = contextSize;
        ConversationTokens = 0;
        TurnCount = 0;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void SetSystemPrompt(string text)
    {
        SystemPromptCalls++;
        SystemPrompt = string.IsNullOrWhiteSpace(text) ? null : text;
        TurnCount = 0;
        ConversationTokens = 0;
    }

    /// <inheritdoc />
    public void ClearConversation()
    {
        ClearCalls++;
        TurnCount = 0;
        ConversationTokens = 0;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatTurnUpdate> SendAsync(string userText, CancellationToken cancellationToken = default)
    {
        if (SendFailure != null) { throw SendFailure; }

        _sent.Add(userText);

        return StreamAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        IsDisposed = true;
        SetState(ModelHostState.Stopped);

        return ValueTask.CompletedTask;
    }

    private async IAsyncEnumerable<ChatTurnUpdate> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        SetState(ModelHostState.Generating);

        try
        {
            for (var index = 0; index < _updates.Count; index++)
            {
                if (index == PauseBeforeUpdate)
                {
                    _reachedPause.TrySetResult();
                    await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

                yield return _updates[index];
            }

            if (TurnFailure != null) { throw TurnFailure; }

            TurnCount++;
        }
        finally
        {
            SetState(ModelHostState.Ready);
        }
    }

    private void SetState(ModelHostState state)
    {
        if (State == state) { return; }

        State = state;
        StateChanged?.Invoke(this, state);
    }
}

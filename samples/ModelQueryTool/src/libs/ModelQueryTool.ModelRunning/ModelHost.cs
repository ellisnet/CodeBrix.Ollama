using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;

namespace ModelQueryTool.ModelRunning;

/// <summary>
/// The one implementation of <see cref="IModelHost"/>: it owns the loaded model and the conversation held
/// with it.
/// </summary>
/// <remarks>
/// <para>
/// The conversation is one growing list of messages and all of it is sent every turn, oldest first, because
/// that is what lets the model runner's prefix cache evaluate only what is new. The system prompt, when
/// there is one, is the first message of every request; an assistant reply is kept as its answer text only,
/// never its reasoning; and a turn that failed or was cancelled leaves the conversation exactly as it was,
/// so the same question can simply be asked again.
/// </para>
/// <para>
/// The chat template is the model file's own, rendered by the model runner. This type never builds prompt
/// markup of its own.
/// </para>
/// </remarks>
public sealed class ModelHost : IModelHost
{
    private const int LargestReserve = 1024;
    private const int TurnUnwindSeconds = 30;

    private readonly ModelHostOptions _options;
    private readonly Func<ModelRunnerOptions, CancellationToken, Task<IRunningModel>> _loader;
    private readonly SemaphoreSlim _lifecycle = new SemaphoreSlim(1, 1);
    private readonly object _gate = new object();
    private readonly List<ChatMessage> _history = new List<ChatMessage>();

    private IRunningModel _model;
    private ModelDetails _details;
    private ModelHostState _state;
    private CancellationTokenSource _turnCancellation;
    private TaskCompletionSource _turnFinished;
    private string _modelPath;
    private string _systemPrompt;
    private uint _contextSize;
    private bool _think;
    private int _conversationTokens;
    private bool _disposed;

    /// <summary>Creates a host that loads models with the model runner itself.</summary>
    /// <param name="options">What to load with and what to start a conversation with, or null for the defaults.</param>
    public ModelHost(ModelHostOptions options = null)
        : this(options, ModelRunner.LoadAsync)
    {
    }

    /// <summary>Creates a host that loads models through the given loader, which is how the tests hand it a model of their own.</summary>
    /// <param name="options">What to load with and what to start a conversation with, or null for the defaults.</param>
    /// <param name="loader">Turns load options into a loaded model.</param>
    /// <exception cref="ArgumentNullException">The loader is null.</exception>
    internal ModelHost(ModelHostOptions options, Func<ModelRunnerOptions, CancellationToken, Task<IRunningModel>> loader)
    {
        if (loader == null)
        {
            throw new ArgumentNullException(nameof(loader));
        }

        _options = options ?? new ModelHostOptions();
        _loader = loader;
        _contextSize = _options.ContextSize;
        _think = _options.Think;
        _systemPrompt = Normalize(_options.SystemPrompt);
    }

    /// <inheritdoc />
    public event EventHandler<ModelHostState> StateChanged;

    /// <inheritdoc />
    public ModelHostState State
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                return _state;
            }
        }
    }

    /// <inheritdoc />
    public ModelDetails Details
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                return _details;
            }
        }
    }

    /// <inheritdoc />
    public string ModelPath
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                return _modelPath;
            }
        }
    }

    /// <inheritdoc />
    public uint ContextSize
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                return _contextSize;
            }
        }
    }

    /// <inheritdoc />
    public int TrainedContextSize
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                return _details == null ? 0 : _details.TrainingContextLength;
            }
        }
    }

    /// <inheritdoc />
    public bool Think
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                return _think;
            }
        }

        set
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                _think = value;
            }
        }
    }

    /// <inheritdoc />
    public string SystemPrompt
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                return _systemPrompt;
            }
        }
    }

    /// <inheritdoc />
    public int TurnCount
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                int count = 0;

                foreach (ChatMessage message in _history)
                {
                    if (message.Role == ChatRole.User)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    /// <inheritdoc />
    public int ConversationTokens
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                return _conversationTokens;
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(
        string modelPath, IProgress<float> loadProgress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ModelRunningException("A model cannot be started without the path of a model file.");
        }

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();
            uint contextSize;

            lock (_gate)
            {
                if (_state != ModelHostState.Stopped && _state != ModelHostState.Faulted)
                {
                    throw new ModelRunningException(
                        $"The model host is {_state}; a model can only be started when none is loaded.");
                }

                contextSize = _contextSize;
            }

            await StartCoreAsync(modelPath, contextSize, loadProgress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        ThrowIfDisposed();
        await _lifecycle.WaitAsync().ConfigureAwait(false);

        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReloadAsync(
        uint contextSize, IProgress<float> loadProgress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();
            string modelPath;
            uint previousSize;
            int trainedSize;

            lock (_gate)
            {
                if (_state != ModelHostState.Ready && _state != ModelHostState.Generating)
                {
                    throw new ModelRunningException(
                        $"The model host is {_state}; the context size can only be changed while a model is loaded.");
                }

                modelPath = _modelPath;
                previousSize = _contextSize;
                trainedSize = _details == null ? 0 : _details.TrainingContextLength;
            }

            if (contextSize < ModelHostOptions.MinimumContextSize)
            {
                throw new ModelRunningException(
                    $"A context size of {contextSize} is too small; the smallest this application accepts is "
                    + $"{ModelHostOptions.MinimumContextSize}.");
            }

            if (trainedSize > 0 && contextSize > (uint)trainedSize)
            {
                throw new ModelRunningException(
                    $"A context size of {contextSize} is larger than the {trainedSize} this model was trained for.");
            }

            await StopCoreAsync().ConfigureAwait(false);

            try
            {
                await StartCoreAsync(modelPath, contextSize, loadProgress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // The cache grows with the context size, so a size that is too large simply will not load.
                // Put the model back the way it was rather than leave the user with nothing; the token is
                // deliberately not honoured here, for the same reason.
                try
                {
                    await StartCoreAsync(modelPath, previousSize, loadProgress, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception fallbackException)
                {
                    ChangeState(ModelHostState.Faulted);

                    throw new ModelRunningException(
                        $"The model would not load with a context size of {contextSize} ({Reason(exception)}), and "
                        + $"loading it again at {previousSize} failed as well ({Reason(fallbackException)}). No model "
                        + "is loaded.",
                        exception);
                }

                throw new ModelRunningException(
                    $"The model would not load with a context size of {contextSize} ({Reason(exception)}). It is "
                    + $"loaded again at {previousSize}, with a new conversation.",
                    exception);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public void SetSystemPrompt(string text)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_state == ModelHostState.Generating)
            {
                throw new ModelRunningException(
                    "A turn is being written; the system prompt can only be changed between turns.");
            }

            _systemPrompt = Normalize(text);
            ClearConversationLocked();
        }
    }

    /// <inheritdoc />
    public void ClearConversation()
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_state == ModelHostState.Generating)
            {
                throw new ModelRunningException(
                    "A turn is being written; the conversation can only be cleared between turns.");
            }

            ClearConversationLocked();
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatTurnUpdate> SendAsync(string userText, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(userText))
        {
            throw new ModelRunningException("There is nothing to send: the message is empty.");
        }

        lock (_gate)
        {
            RequireReadyLocked();
        }

        return SendCoreAsync(userText, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await _lifecycle.WaitAsync().ConfigureAwait(false);

        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }

    private async Task StartCoreAsync(
        string modelPath, uint contextSize, IProgress<float> loadProgress, CancellationToken cancellationToken)
    {
        ChangeState(ModelHostState.Starting);
        IRunningModel model;

        try
        {
            model = await _loader(BuildRunnerOptions(modelPath, contextSize, loadProgress), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ChangeState(ModelHostState.Stopped);
            throw;
        }
        catch (Exception exception)
        {
            ChangeState(ModelHostState.Stopped);

            throw new ModelRunningException(
                $"The model at '{modelPath}' could not be loaded with a context size of {contextSize}. "
                + Reason(exception),
                exception);
        }

        if (model == null)
        {
            ChangeState(ModelHostState.Stopped);

            throw new ModelRunningException(
                $"The model at '{modelPath}' could not be loaded: nothing was handed back.");
        }

        bool changed;

        lock (_gate)
        {
            _model = model;
            _details = model.Details;
            _modelPath = modelPath;
            _contextSize = contextSize;
            ClearConversationLocked();
            changed = SetStateLocked(ModelHostState.Ready);
        }

        if (changed)
        {
            RaiseStateChanged(ModelHostState.Ready);
        }
    }

    private async Task StopCoreAsync()
    {
        IRunningModel model;
        CancellationTokenSource turnCancellation;
        Task turnFinished;
        bool changed;

        lock (_gate)
        {
            if (_state == ModelHostState.Stopped)
            {
                return;
            }

            model = _model;
            turnCancellation = _turnCancellation;
            turnFinished = _turnFinished == null ? null : _turnFinished.Task;
            changed = SetStateLocked(ModelHostState.Stopping);
        }

        if (changed)
        {
            RaiseStateChanged(ModelHostState.Stopping);
        }

        if (turnCancellation != null)
        {
            try
            {
                turnCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The turn unwound on its own between reading it and cancelling it.
            }
        }

        if (turnFinished != null && !turnFinished.IsCompleted)
        {
            // A turn whose consumer has stopped reading it cannot unwind, so the wait is bounded; unloading a
            // model with a stream still open is something the model runner handles by itself.
            using (CancellationTokenSource unwind = new CancellationTokenSource())
            {
                await Task.WhenAny(turnFinished, Task.Delay(TimeSpan.FromSeconds(TurnUnwindSeconds), unwind.Token))
                    .ConfigureAwait(false);
                unwind.Cancel();
            }
        }

        if (model != null)
        {
            try
            {
                await model.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Unloading never stops the host from reaching a stopped state.
            }
        }

        lock (_gate)
        {
            _model = null;
            _details = null;
            _modelPath = null;
            ClearConversationLocked();
            changed = SetStateLocked(ModelHostState.Stopped);
        }

        if (changed)
        {
            RaiseStateChanged(ModelHostState.Stopped);
        }
    }

    private async IAsyncEnumerable<ChatTurnUpdate> SendCoreAsync(
        string userText, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IRunningModel model;
        CancellationTokenSource turnCancellation;
        TaskCompletionSource turnFinished;
        bool think;
        uint contextSize;

        lock (_gate)
        {
            ThrowIfDisposedLocked();
            RequireReadyLocked();
            model = _model;
            think = _think;
            contextSize = _contextSize;
            turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            turnFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _turnCancellation = turnCancellation;
            _turnFinished = turnFinished;
            SetStateLocked(ModelHostState.Generating);
        }

        RaiseStateChanged(ModelHostState.Generating);

        try
        {
            await foreach (ChatTurnUpdate update in
                RunTurnAsync(model, userText, think, contextSize, turnCancellation.Token).ConfigureAwait(false))
            {
                yield return update;
            }
        }
        finally
        {
            bool changed;

            lock (_gate)
            {
                if (ReferenceEquals(_turnCancellation, turnCancellation))
                {
                    _turnCancellation = null;
                }

                changed = _state == ModelHostState.Generating && SetStateLocked(ModelHostState.Ready);
            }

            turnCancellation.Dispose();

            if (changed)
            {
                RaiseStateChanged(ModelHostState.Ready);
            }

            turnFinished.TrySetResult();
        }
    }

    private async IAsyncEnumerable<ChatTurnUpdate> RunTurnAsync(
        IRunningModel model,
        string userText,
        bool think,
        uint contextSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ChatMessage userMessage = new ChatMessage(ChatRole.User, userText);
        List<ChatMessage> history;
        string systemPrompt;

        lock (_gate)
        {
            history = new List<ChatMessage>(_history);
            systemPrompt = _systemPrompt;
        }

        int reserve = (int)Math.Min((uint)LargestReserve, contextSize / 4u);
        int dropped = 0;
        ChatRequest request = BuildRequest(systemPrompt, history, userMessage, think);

        while (await CountPromptTokensAsync(model, request, cancellationToken).ConfigureAwait(false) + reserve
               > (long)contextSize)
        {
            if (!TryDropOldestTurn(history))
            {
                throw new ModelRunningException(
                    $"This message does not fit in a context of {contextSize} tokens, even on its own. Shorten it, "
                    + "or raise the context size.");
            }

            dropped++;
            request = BuildRequest(systemPrompt, history, userMessage, think);
        }

        if (dropped > 0)
        {
            yield return new ChatTurnUpdate
            {
                Kind = ChatTurnUpdateKind.Notice,
                Text = dropped == 1
                    ? "1 earlier turn was left out of this request to make room in the context; this turn is slower, "
                        + "because the prefix cache starts again."
                    : $"{dropped} earlier turns were left out of this request to make room in the context; this turn "
                        + "is slower, because the prefix cache starts again.",
            };
        }

        StringBuilder content = new StringBuilder();
        FinishReason finishReason = FinishReason.None;
        GenerationStatistics statistics = null;
        IAsyncEnumerator<ChatUpdate> updates =
            model.ChatAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                bool moved;

                try
                {
                    moved = await updates.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new ModelRunningException(
                        "The model stopped part way through its answer. " + Reason(exception), exception);
                }

                if (!moved)
                {
                    break;
                }

                ChatUpdate update = updates.Current;

                if (update == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(update.ThinkingDelta))
                {
                    yield return new ChatTurnUpdate
                    {
                        Kind = ChatTurnUpdateKind.Thinking,
                        Text = update.ThinkingDelta,
                    };
                }

                if (!string.IsNullOrEmpty(update.ContentDelta))
                {
                    content.Append(update.ContentDelta);

                    yield return new ChatTurnUpdate
                    {
                        Kind = ChatTurnUpdateKind.Content,
                        Text = update.ContentDelta,
                    };
                }

                if (update.IsFinal)
                {
                    finishReason = update.FinishReason;
                    statistics = update.Statistics;
                }
            }
        }
        finally
        {
            await updates.DisposeAsync().ConfigureAwait(false);
        }

        lock (_gate)
        {
            _history.Clear();
            _history.AddRange(history);
            _history.Add(userMessage);
            _history.Add(new ChatMessage(ChatRole.Assistant, content.ToString()));

            if (statistics != null)
            {
                _conversationTokens = statistics.PromptTokens + statistics.GeneratedTokens;
            }
        }

        yield return new ChatTurnUpdate
        {
            Kind = ChatTurnUpdateKind.Completed,
            FinishReason = finishReason,
            Statistics = statistics,
        };
    }

    private static async Task<int> CountPromptTokensAsync(
        IRunningModel model, ChatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            string prompt = await model.RenderChatPromptAsync(request, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<int> tokens = await model
                .TokenizeAsync(prompt, false, true, cancellationToken)
                .ConfigureAwait(false);

            return tokens == null ? 0 : tokens.Count;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ModelRunningException(
                "The conversation could not be measured against the context size. " + Reason(exception), exception);
        }
    }

    private static ChatRequest BuildRequest(
        string systemPrompt, List<ChatMessage> history, ChatMessage userMessage, bool think)
    {
        ChatRequest request = new ChatRequest { Think = think };

        if (systemPrompt != null)
        {
            request.Messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
        }

        foreach (ChatMessage message in history)
        {
            request.Messages.Add(message);
        }

        request.Messages.Add(userMessage);

        return request;
    }

    private static bool TryDropOldestTurn(List<ChatMessage> history)
    {
        if (history.Count == 0)
        {
            return false;
        }

        history.RemoveAt(0);

        while (history.Count > 0 && history[0].Role != ChatRole.User)
        {
            history.RemoveAt(0);
        }

        return true;
    }

    private static string Normalize(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string Reason(Exception exception)
    {
        return exception is ModelRunningException && exception.InnerException != null
            ? exception.InnerException.Message
            : exception.Message;
    }

    private ModelRunnerOptions BuildRunnerOptions(string modelPath, uint contextSize, IProgress<float> loadProgress)
    {
        return new ModelRunnerOptions
        {
            ModelPath = modelPath,
            ContextSize = contextSize,
            GpuLayers = 0,
            LoadMode = ModelLoadMode.MemoryMap,
            UseExtraBufferTypes = _options.UseExtraBufferTypes,
            Threads = _options.Threads,

            // Called on the engine's own loading thread, so nothing is done with the fraction here but hand
            // it to whoever asked for it.
            LoadProgress = loadProgress,
        };
    }

    private void ChangeState(ModelHostState state)
    {
        bool changed;

        lock (_gate)
        {
            changed = SetStateLocked(state);
        }

        if (changed)
        {
            RaiseStateChanged(state);
        }
    }

    private bool SetStateLocked(ModelHostState state)
    {
        if (_state == state)
        {
            return false;
        }

        _state = state;

        return true;
    }

    private void RaiseStateChanged(ModelHostState state)
    {
        EventHandler<ModelHostState> handler = StateChanged;

        if (handler != null)
        {
            handler(this, state);
        }
    }

    private void ClearConversationLocked()
    {
        _history.Clear();
        _conversationTokens = 0;
    }

    private void RequireReadyLocked()
    {
        if (_state != ModelHostState.Ready)
        {
            throw new ModelRunningException(
                $"The model host is {_state}; a message can only be sent when it is {ModelHostState.Ready}.");
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
        {
            ThrowIfDisposedLocked();
        }
    }

    private void ThrowIfDisposedLocked()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

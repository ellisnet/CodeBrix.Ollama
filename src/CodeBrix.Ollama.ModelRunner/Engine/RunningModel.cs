using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The engine behind <see cref="IRunningModel"/>: one loaded model, one inference context, and the decode
/// loop that turns a prompt into tokens.
/// </summary>
/// <remarks>
/// <para>
/// Three rules shape the whole type. Every native call for this model is made on one thread, because a
/// <c>llama_context</c> is not re-entrant (see <see cref="EngineWorker"/>). Every request holds a gate for
/// its whole duration, because the context's key/value memory is request state and two interleaved requests
/// would read each other's history. Generation takes that gate without waiting, before prompt preparation;
/// a busy model refuses another generation with <see cref="InferenceException"/>. A decode already under way is stopped through the engine's abort
/// callback rather than by waiting for it, because evaluating the prompt of a large model is one call that
/// can run for minutes (see <see cref="EngineAbortFlag"/>).
/// </para>
/// <para>
/// The chat layer - <see cref="ChatAsync"/>, <see cref="ChatToEndAsync"/> and
/// <see cref="RenderChatPromptAsync"/> - uses the same decode loop as <see cref="GenerateFromTokensAsync"/>,
/// taking tokens that something else has already rendered and tokenized. A chat request is
/// rendered by <see cref="ChatTemplateRenderer"/>, tokenized under the rule in <see cref="ChatBosRule"/>,
/// decoded by that loop, and taken apart again by <see cref="ChatPipeline"/>.
/// </para>
/// </remarks>
internal sealed class RunningModel : IRunningModel
{
    private const int DisposeGateWaitMilliseconds = 30_000;

    private static readonly AsyncLocal<EngineRequestScope> Scope = new AsyncLocal<EngineRequestScope>();

    private readonly EngineWorker worker;
    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    private readonly EngineAbortFlag abort;
    private readonly PrefixCache cache = new PrefixCache();
    private readonly Dictionary<string, SafeLlamaAdapterHandle> adapters =
        new Dictionary<string, SafeLlamaAdapterHandle>(StringComparer.Ordinal);

    private readonly IntPtr model;
    private readonly IntPtr context;
    private readonly IntPtr vocab;
    private readonly IntPtr memory;
    private readonly int contextLength;
    private readonly int batchLength;
    private readonly ChatTemplateRenderer renderer;
    private readonly bool vocabularyAddsBos;
    private readonly bool vocabularyHasTokenizer;
    private readonly string beginningOfSequenceText;

    private SafeLlamaModelHandle modelHandle;
    private SafeLlamaContextHandle contextHandle;
    private SafeLlamaContextHandle embeddingContextHandle;
    private LlamaBatchBuffer batch;
    private EngineSamplerChain activeChain;
    private IntPtr embeddingContext;
    private int disposed;

    private RunningModel(
        EngineWorker worker,
        ModelRunnerOptions options,
        SafeLlamaModelHandle modelHandle,
        SafeLlamaContextHandle contextHandle,
        EngineAbortFlag abort,
        ModelDetails details,
        ChatTemplateDialect dialect)
    {
        this.worker = worker;
        this.abort = abort;
        this.modelHandle = modelHandle;
        this.contextHandle = contextHandle;

        Options = options;
        Details = details;
        ChatTemplateDialect = dialect;

        model = modelHandle.DangerousGetHandle();
        context = contextHandle.DangerousGetHandle();
        vocab = NativeMethods.llama_model_get_vocab(model);
        memory = NativeMethods.llama_get_memory(context);

        contextLength = (int)NativeMethods.llama_n_ctx_seq(context);
        if (contextLength <= 0) contextLength = (int)NativeMethods.llama_n_ctx(context);

        batchLength = Math.Max(1, (int)NativeMethods.llama_n_batch(context));
        batch = new LlamaBatchBuffer(batchLength, 1);

        // Read on the worker thread, once, while the model is being built: a chat template needs the two
        // token texts on every render and the beginning-of-sequence rule needs the flag on every request.
        vocabularyAddsBos = NativeMethods.llama_vocab_get_add_bos(vocab);

        // A file can carry weights and no tokenizer at all, and the engine's text calls assert rather than
        // fail on such a vocabulary - GGML_ASSERT ends the process. Everything that turns tokens into text
        // or text into tokens is therefore asked this first.
        vocabularyHasTokenizer = NativeMethods.llama_vocab_type(vocab) != LlamaVocabType.None;

        beginningOfSequenceText = TokenText(vocab, NativeMethods.llama_vocab_bos(vocab));

        renderer = new ChatTemplateRenderer(
            dialect,
            options.JinjaTemplate,
            options.OllamaTemplate,
            details.ChatTemplate,
            beginningOfSequenceText,
            TokenText(vocab, NativeMethods.llama_vocab_eos(vocab)));
    }

    /// <inheritdoc />
    public ModelDetails Details { get; }

    /// <inheritdoc />
    public ModelRunnerOptions Options { get; }

    /// <inheritdoc />
    public ChatTemplateDialect ChatTemplateDialect { get; }

    /// <summary>The context size the engine settled on, which is never below its own minimum of 256.</summary>
    internal int ContextLength => contextLength;

    /// <summary>The logical batch size the engine settled on.</summary>
    internal int BatchLength => batchLength;

    /// <summary>How many tokens of the last prompt are still in the context's memory.</summary>
    internal int CachedTokenCount => cache.Count;

    /// <summary>How wide one embedding vector is.</summary>
    /// <param name="outputWidth">What the engine reports as the model's embedding output width.</param>
    /// <param name="hiddenWidth">What the engine reports as the model's hidden width.</param>
    /// <returns>The width, which is the output width wherever the engine has one.</returns>
    /// <remarks>
    /// A model with a projection on top of its hidden state - which is what a model trained to embed has -
    /// writes vectors of the projected width, and reading the hidden width instead copies the wrong number
    /// of floats out of the engine's buffer. The hidden width is the fallback for a build old enough not to
    /// answer the newer question, where the two are the same number anyway.
    /// </remarks>
    internal static int EmbeddingWidth(int outputWidth, int hiddenWidth)
    {
        return outputWidth > 0 ? outputWidth : hiddenWidth;
    }

    /// <summary>How many tokens one input to be embedded may have.</summary>
    /// <param name="contextRoom">The embedding context's sequence length.</param>
    /// <param name="physicalBatch">The embedding context's physical batch size.</param>
    /// <returns>The limit, never below one.</returns>
    /// <remarks>
    /// The physical batch, not the logical one, is the limit. An encoder is computed in one shot and a model
    /// whose attention is not causal cannot be split into micro-batches at all, and the engine asserts both
    /// of those rather than returning an error - <c>llama-context.cpp</c>'s "encoder requires n_ubatch >=
    /// n_tokens" and "non-causal attention requires n_ubatch >= n_tokens" end the process rather than the
    /// request. An input over the limit is refused here, where it can be explained.
    /// </remarks>
    internal static int EmbeddingInputLimit(int contextRoom, int physicalBatch)
    {
        int physical = Math.Max(1, physicalBatch);
        if (contextRoom <= 0) return physical;

        return Math.Max(1, Math.Min(contextRoom, physical));
    }

    /// <summary>
    /// Loads a model and builds the running model around it. Runs on the worker thread, which from this
    /// point on owns every handle it creates.
    /// </summary>
    /// <param name="worker">The worker thread, which the result takes ownership of.</param>
    /// <param name="options">The options, already validated.</param>
    /// <param name="cancellationToken">A token that aborts the load through the engine's progress callback.</param>
    /// <returns>The running model.</returns>
    /// <exception cref="ModelLoadException">The engine would not load the model or create a context.</exception>
    internal static unsafe RunningModel LoadOnWorker(
        EngineWorker worker, ModelRunnerOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EngineLog.Clear();

        IntPtr rawModel;
        bool cancelledDuringLoad;

        using (EngineLoadProgress progress = new EngineLoadProgress(options.LoadProgress, cancellationToken))
        {
            LlamaModelParams modelParams = ParameterMapper.BuildModelParams(options, progress);
            rawModel = NativeMethods.llama_model_load_from_file(options.ModelPath, modelParams);
            cancelledDuringLoad = progress.WasCancelled;
        }

        if (rawModel == IntPtr.Zero)
        {
            if (cancelledDuringLoad || cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new ModelLoadException(EngineLog.Describe(
                $"The engine could not load the model at '{options.ModelPath}'."));
        }

        SafeLlamaModelHandle modelHandle = new SafeLlamaModelHandle(rawModel);
        EngineAbortFlag abort = new EngineAbortFlag();
        SafeLlamaContextHandle contextHandle = null;

        try
        {
            LlamaContextParams contextParams =
                ParameterMapper.BuildContextParams(options, abort, options.EmbeddingsMode, null);

            IntPtr rawContext = NativeMethods.llama_init_from_model(rawModel, contextParams);
            if (rawContext == IntPtr.Zero)
            {
                throw new ModelLoadException(EngineLog.Describe(
                    "The engine could not create an inference context for this model. A context size larger "
                    + "than the machine can hold is the usual cause; try a smaller ContextSize or a quantized "
                    + "KeyCacheType and ValueCacheType."));
            }

            contextHandle = new SafeLlamaContextHandle(rawContext);

            ModelDetails details = ModelDetailsBuilder.Build(rawModel, options.ModelPath, false);
            ChatTemplateDialect dialect = ChatTemplateStrategy.Resolve(
                options.ChatTemplateDialect, options.OllamaTemplate, options.JinjaTemplate, details.ChatTemplate);

            RunningModel running = new RunningModel(
                worker, options, modelHandle, contextHandle, abort, details, dialect);

            if (options.LoraAdapters.Count > 0)
            {
                running.ApplyAdaptersOnWorker(new List<LoraAdapterOptions>(options.LoraAdapters));
            }

            return running;
        }
        catch (Exception)
        {
            if (contextHandle != null) contextHandle.Dispose();
            modelHandle.Dispose();
            abort.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<int>> TokenizeAsync(
        string text,
        bool addSpecialTokens = true,
        bool parseSpecialTokens = true,
        CancellationToken cancellationToken = default)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        ThrowIfDisposed();
        ThrowIfNoTokenizer(nameof(TokenizeAsync));

        // Tokenizing reads the vocabulary and never touches the context, so it does not take the request
        // gate: it stays answerable while a generation is running.
        return worker.RunAsync<IReadOnlyList<int>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeText.Tokenize(vocab, text, addSpecialTokens, parseSpecialTokens);
        });
    }

    /// <inheritdoc />
    public Task<string> DetokenizeAsync(
        IReadOnlyList<int> tokens, bool renderSpecialTokens = false, CancellationToken cancellationToken = default)
    {
        if (tokens == null) throw new ArgumentNullException(nameof(tokens));
        ThrowIfDisposed();
        ThrowIfNoTokenizer(nameof(DetokenizeAsync));

        int[] copy = new int[tokens.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = tokens[i];

        return worker.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeText.Detokenize(vocab, copy, false, renderSpecialTokens);
        });
    }

    /// <inheritdoc />
    public IAsyncEnumerable<GenerationUpdate> GenerateAsync(
        string prompt, GenerationOptions options = null, CancellationToken cancellationToken = default)
    {
        if (prompt == null) throw new ArgumentNullException(nameof(prompt));
        ThrowIfDisposed();

        EngineRequestScope scope = BeginStream();
        return ExclusiveGenerationAsync(scope, GenerateTextAsync(prompt, options, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GenerationResult> GenerateToEndAsync(
        string prompt, GenerationOptions options = null, CancellationToken cancellationToken = default)
    {
        StringBuilder text = new StringBuilder();
        FinishReason reason = FinishReason.None;
        GenerationStatistics statistics = null;

        await foreach (GenerationUpdate update in
            GenerateAsync(prompt, options, cancellationToken).ConfigureAwait(false))
        {
            text.Append(update.Text);
            if (!update.IsFinal) continue;

            reason = update.FinishReason;
            statistics = update.Statistics;
        }

        return new GenerationResult
        {
            Text = text.ToString(),
            FinishReason = reason,
            Statistics = statistics,
        };
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatUpdate> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        ThrowIfDisposed();

        EngineRequestScope scope = BeginStream();
        return ExclusiveGenerationAsync(scope, ChatStreamAsync(request, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ChatResponse> ChatToEndAsync(
        ChatRequest request, CancellationToken cancellationToken = default)
    {
        StringBuilder content = new StringBuilder();
        StringBuilder thinking = new StringBuilder();
        IReadOnlyList<ToolCall> calls = Array.Empty<ToolCall>();
        FinishReason reason = FinishReason.None;
        GenerationStatistics statistics = null;

        await foreach (ChatUpdate update in ChatAsync(request, cancellationToken).ConfigureAwait(false))
        {
            content.Append(update.ContentDelta);
            thinking.Append(update.ThinkingDelta);

            if (!update.IsFinal) continue;

            calls = update.ToolCalls;
            reason = update.FinishReason;
            statistics = update.Statistics;
        }

        ChatMessage message = new ChatMessage(ChatRole.Assistant, content.ToString())
        {
            Thinking = thinking.Length == 0 ? null : thinking.ToString(),
        };

        foreach (ToolCall call in calls) message.ToolCalls.Add(call);

        return new ChatResponse
        {
            Message = message,
            FinishReason = reason,
            Statistics = statistics,
        };
    }

    /// <inheritdoc />
    public Task<string> RenderChatPromptAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        ThrowIfDisposed();

        // Rendering reads no context state, so like tokenizing it does not take the request gate; it still
        // runs on the worker because the native dialect calls into the engine to do it.
        return worker.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return renderer.Render(request);
        });
    }

    /// <inheritdoc />
    public Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        if (inputs == null) throw new ArgumentNullException(nameof(inputs));
        ThrowIfDisposed();
        ThrowIfStreaming(nameof(EmbedAsync));
        ThrowIfNoTokenizer(nameof(EmbedAsync));

        string[] copy = new string[inputs.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = inputs[i] ?? string.Empty;

        return GatedAsync(() => AbortableAsync(
            () => worker.RunAsync(() => EmbedOnWorker(copy, cancellationToken)), cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetLoraAdaptersAsync(
        IReadOnlyList<LoraAdapterOptions> adapterOptions, CancellationToken cancellationToken = default)
    {
        if (adapterOptions == null) throw new ArgumentNullException(nameof(adapterOptions));
        ThrowIfDisposed();
        ThrowIfStreaming(nameof(SetLoraAdaptersAsync));

        List<LoraAdapterOptions> copy = new List<LoraAdapterOptions>(adapterOptions);

        return GatedAsync(() => worker.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyAdaptersOnWorker(copy);
        }), cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfStreaming(nameof(ClearCacheAsync));

        return GatedAsync(() => worker.RunAsync(() =>
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            ClearMemoryOnWorker();
        }), cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        bool taken = BeginDispose();

        try
        {
            worker.RunAsync(ReleaseOnWorker).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Unloading is best effort: a worker that has already gone leaves the process to reclaim the
            // memory, and throwing from Dispose would hide whatever the caller was really doing.
        }

        EndDispose(taken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        bool taken = await BeginDisposeAsync().ConfigureAwait(false);

        try
        {
            await worker.RunAsync(ReleaseOnWorker).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // As in Dispose.
        }

        EndDispose(taken);
    }

    /// <summary>
    /// Stops whatever the engine is computing and takes the request gate, so that nothing is freed under a
    /// decode that is still running.
    /// </summary>
    /// <returns>Whether the gate was taken.</returns>
    /// <remarks>
    /// The wait is bounded and the gate is skipped outright when the caller is disposing from inside its own
    /// enumeration, because that enumeration cannot release the gate until this call returns. Freeing is
    /// still safe in that case: an enumeration suspended at a <c>yield return</c> has no native call in
    /// flight, and its next step finds the worker gone and ends with
    /// <see cref="ObjectDisposedException"/>.
    /// </remarks>
    private bool BeginDispose()
    {
        abort.Raise();
        if (StreamingOnThisChain) return false;

        return gate.Wait(DisposeGateWaitMilliseconds);
    }

    /// <summary>The awaiting form of <see cref="BeginDispose"/>.</summary>
    /// <returns>Whether the gate was taken.</returns>
    private async Task<bool> BeginDisposeAsync()
    {
        abort.Raise();
        if (StreamingOnThisChain) return false;

        return await gate.WaitAsync(DisposeGateWaitMilliseconds).ConfigureAwait(false);
    }

    /// <summary>Shuts the worker down and, when the gate was taken, releases and frees it.</summary>
    /// <param name="taken">Whether <see cref="BeginDispose"/> took the gate.</param>
    /// <remarks>
    /// A gate this call never took still belongs to an enumeration that will release it, and freeing it
    /// underneath that enumeration would turn its orderly ending into an exception from a <c>finally</c>.
    /// A <see cref="SemaphoreSlim"/> that is never disposed is collected with everything else.
    /// </remarks>
    private void EndDispose(bool taken)
    {
        worker.Dispose();

        if (!taken) return;

        gate.Release();
        gate.Dispose();
    }

    /// <summary>
    /// The decode loop over a prompt something else has already rendered and tokenized. This is the seam the
    /// chat wave sits on: it renders the conversation, tokenizes it, and feeds the tokens straight in.
    /// </summary>
    /// <param name="promptTokens">The prompt's tokens, in order.</param>
    /// <param name="options">The generation controls, or <see langword="null"/> for the defaults.</param>
    /// <param name="promptHasBos">
    /// Whether <paramref name="promptTokens"/> already starts with the vocabulary's beginning-of-sequence
    /// token. When it does not and the vocabulary asks for one, the engine prepends it.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the generation.</param>
    /// <returns>Updates as text is produced; the last one has <see cref="GenerationUpdate.IsFinal"/> set.</returns>
    internal IAsyncEnumerable<GenerationUpdate> GenerateFromTokensAsync(
        IReadOnlyList<int> promptTokens,
        GenerationOptions options,
        bool promptHasBos,
        CancellationToken cancellationToken = default)
    {
        if (promptTokens == null) throw new ArgumentNullException(nameof(promptTokens));
        ThrowIfDisposed();

        // The chain is marked here rather than in the iterator below: an async method's changes to an
        // AsyncLocal are undone when its state machine returns, so a marker set inside the iterator would
        // never reach the caller that is about to enumerate it.
        EngineRequestScope scope = BeginStream();

        int[] copy = new int[promptTokens.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = promptTokens[i];

        return ExclusiveGenerationAsync(scope,
            StreamFromTokensAsync(copy, options, promptHasBos, cancellationToken), cancellationToken);
    }

    private async IAsyncEnumerable<T> ExclusiveGenerationAsync<T>(
        EngineRequestScope scope,
        IAsyncEnumerable<T> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Acquire before chat rendering or tokenization can queue work on the native thread. Acquisition
        // belongs to enumeration, so creating an enumerable without reading it never occupies the model.
        ThrowIfDisposed();
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InferenceException(
                "This model is already in use. A second generation is refused rather than queued; finish or"
                + " dispose the active enumeration, or load another model instance to generate concurrently.");
        scope.StreamHoldsTheGate = true;
        try
        {
            ThrowIfDisposed();
            await foreach (T update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return update;
        }
        finally
        {
            scope.StreamHoldsTheGate = false;
            gate.Release();
        }
    }

    private async IAsyncEnumerable<GenerationUpdate> StreamFromTokensAsync(
        int[] promptTokens,
        GenerationOptions options,
        bool promptHasBos,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int[] tokens = await worker
            .RunAsync(() => ApplyBeginningOfSequenceRule(promptTokens, promptHasBos))
            .ConfigureAwait(false);

        await foreach (GenerationUpdate update in
            DecodeAsync(tokens, options, cancellationToken).ConfigureAwait(false))
            yield return update;
    }

    /// <summary>
    /// Decodes a fixed run of tokens and returns every position's logits. The hook the conformance test uses
    /// to reach the decode path of a model whose file carries no tokenizer at all.
    /// </summary>
    /// <param name="tokens">The token ids to decode, from position zero.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>One row of <c>n_vocab</c> logits per token, in order.</returns>
    internal Task<float[][]> DecodeForLogitsAsync(
        IReadOnlyList<int> tokens, CancellationToken cancellationToken = default)
    {
        if (tokens == null) throw new ArgumentNullException(nameof(tokens));
        ThrowIfDisposed();

        int[] copy = new int[tokens.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = tokens[i];

        return GatedAsync(() => AbortableAsync(
            () => worker.RunAsync(() => DecodeForLogitsOnWorker(copy, cancellationToken)), cancellationToken),
            cancellationToken);
    }

    private async IAsyncEnumerable<ChatUpdate> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Stopwatch preparation = Stopwatch.StartNew();

        string prompt = await worker.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return renderer.Render(request);
        }).ConfigureAwait(false);

        // The rendered prompt decides whether the tokenizer may add a beginning-of-sequence token; special
        // tokens are always parsed, because a chat prompt is nothing but special tokens and text.
        bool addSpecial = ChatBosRule.AddSpecialTokens(prompt, vocabularyAddsBos, beginningOfSequenceText);

        int[] tokens = await worker.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeText.Tokenize(vocab, prompt, addSpecial, true);
        }).ConfigureAwait(false);

        GenerationOptions options = BuildChatOptions(request);
        ChatPipeline pipeline = ChatPipeline.Create(
            request, renderer.Dialect, renderer.TemplateText, prompt);

        preparation.Stop();
        TimeSpan rendering = preparation.Elapsed;

        // The chat layer owns the beginning-of-sequence decision outright, so the decode loop is told the
        // prompt already carries whatever it needs and adds nothing of its own.
        await foreach (GenerationUpdate update in
            StreamFromTokensAsync(tokens, options, true, cancellationToken).ConfigureAwait(false))
        {
            ChatPipelineOutput step = pipeline.Add(update.Text);

            if (!update.IsFinal)
            {
                if (step.HasText)
                {
                    yield return new ChatUpdate
                    {
                        ContentDelta = step.Content,
                        ThinkingDelta = step.Thinking,
                    };
                }

                continue;
            }

            ChatPipelineOutput tail = pipeline.Flush();

            yield return new ChatUpdate
            {
                ContentDelta = step.Content + tail.Content,
                ThinkingDelta = step.Thinking + tail.Thinking,
                ToolCalls = pipeline.ToolCalls,
                IsFinal = true,
                FinishReason = pipeline.ToolCalls.Count > 0 ? FinishReason.ToolCall : update.FinishReason,
                Statistics = WithRenderingTime(update.Statistics, rendering),
            };
        }
    }

    /// <summary>
    /// The generation controls one chat request runs under: the caller's own, with the grammar the request
    /// asked for resolved and the template's stop strings added.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The options, which are a copy: the caller's object is never written to.</returns>
    private GenerationOptions BuildChatOptions(ChatRequest request)
    {
        GenerationOptions source = request.Options ?? new GenerationOptions();

        GenerationOptions effective = new GenerationOptions
        {
            MaxTokens = source.MaxTokens,
            Sampling = source.Sampling,
            Grammar = EngineGrammar.Select(source, request.ResponseFormat),
        };

        foreach (string stop in source.StopSequences)
        {
            if (!string.IsNullOrEmpty(stop)) effective.StopSequences.Add(stop);
        }

        foreach (string stop in renderer.StopStrings)
        {
            if (!string.IsNullOrEmpty(stop) && !effective.StopSequences.Contains(stop))
            {
                effective.StopSequences.Add(stop);
            }
        }

        return effective;
    }

    /// <summary>
    /// The decode loop's statistics with the time spent rendering and tokenizing folded into the total,
    /// which is what <see cref="GenerationStatistics.TotalDuration"/> promises.
    /// </summary>
    /// <param name="statistics">The statistics the decode loop produced.</param>
    /// <param name="rendering">How long rendering and tokenizing took.</param>
    /// <returns>The statistics.</returns>
    private static GenerationStatistics WithRenderingTime(GenerationStatistics statistics, TimeSpan rendering)
    {
        if (statistics == null) return null;

        return new GenerationStatistics
        {
            PromptTokens = statistics.PromptTokens,
            CachedPromptTokens = statistics.CachedPromptTokens,
            GeneratedTokens = statistics.GeneratedTokens,
            PromptDuration = statistics.PromptDuration,
            GenerationDuration = statistics.GenerationDuration,
            TotalDuration = statistics.TotalDuration + rendering,
        };
    }

    /// <summary>The text of one token, as the vocabulary spells it.</summary>
    /// <param name="vocabulary">The vocabulary.</param>
    /// <param name="token">The token id, which may be negative when the vocabulary has no such token.</param>
    /// <returns>The text, or an empty string.</returns>
    private static unsafe string TokenText(IntPtr vocabulary, int token)
    {
        if (token < 0) return string.Empty;

        byte* text = NativeMethods.llama_vocab_get_text(vocabulary, token);
        if (text == null) return string.Empty;

        return NativeLibraryLoader.ReadUtf8(text) ?? string.Empty;
    }

    private async IAsyncEnumerable<GenerationUpdate> GenerateTextAsync(
        string prompt,
        GenerationOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // llama.cpp's own front ends tokenize a raw completion prompt with add_special and parse_special
        // both true: the vocabulary's add_bos flag decides whether a beginning-of-sequence token is added.
        int[] tokens = await worker.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeText.Tokenize(vocab, prompt, true, true);
        }).ConfigureAwait(false);

        await foreach (GenerationUpdate update in
            DecodeAsync(tokens, options, cancellationToken).ConfigureAwait(false))
            yield return update;
    }

    private async IAsyncEnumerable<GenerationUpdate> DecodeAsync(
        IReadOnlyList<int> promptTokens,
        GenerationOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        GenerationOptions settings = options ?? new GenerationOptions();
        string grammar = EngineGrammar.Select(settings);

        Utf8Assembler assembler = new Utf8Assembler();
        StopSequenceDetector stops = new StopSequenceDetector(settings.StopSequences);
        List<int> pending = new List<int>();

        Stopwatch total = Stopwatch.StartNew();
        Stopwatch clock = new Stopwatch();

        // The flag is cleared at the start of every operation that decodes and never at the end of one: a
        // token cancelled between the reset and the disposal of the registration below would otherwise
        // raise it again and leave the next request aborting before it began.
        abort.Reset();
        EngineSamplerChain chain = null;

        using (cancellationToken.Register(RaiseAbort))
        {
            try
            {
                clock.Restart();
                int cached = await worker
                    .RunAsync(() => EvaluatePromptOnWorker(promptTokens, cancellationToken))
                    .ConfigureAwait(false);
                clock.Stop();

                TimeSpan promptDuration = clock.Elapsed;

                chain = await worker
                    .RunAsync(() => EngineSamplerChain.Create(vocab, settings.Sampling, grammar))
                    .ConfigureAwait(false);

                // The model owns the chain for as long as it exists, so that a Dispose that comes in the
                // middle of this request frees it on the worker rather than leaving it to leak.
                Interlocked.Exchange(ref activeChain, chain);

                IntPtr chainHandle = chain.Handle();

                int generated = 0;
                FinishReason reason = FinishReason.Stop;
                clock.Restart();

                while (true)
                {
                    EngineStep step = await worker
                        .RunAsync(() => StepOnWorker(chainHandle, cancellationToken))
                        .ConfigureAwait(false);

                    if (step.IsEndOfGeneration)
                    {
                        reason = FinishReason.Stop;
                        break;
                    }

                    generated++;
                    pending.Add(step.Token);

                    string emitted = stops.Append(assembler.Append(step.Bytes));
                    if (emitted.Length > 0)
                    {
                        GenerationUpdate update = new GenerationUpdate
                        {
                            Text = emitted,
                            Tokens = pending.ToArray(),
                        };

                        pending.Clear();
                        yield return update;
                    }

                    if (stops.IsStopped)
                    {
                        reason = FinishReason.StopSequence;
                        break;
                    }

                    if (settings.MaxTokens.HasValue && generated >= settings.MaxTokens.Value)
                    {
                        reason = FinishReason.Length;
                        break;
                    }

                    bool advanced = await worker
                        .RunAsync(() => AdvanceOnWorker(step.Token, cancellationToken))
                        .ConfigureAwait(false);

                    if (!advanced)
                    {
                        reason = FinishReason.ContextFull;
                        break;
                    }
                }

                clock.Stop();
                total.Stop();

                string tail = string.Empty;
                if (!stops.IsStopped)
                {
                    tail = stops.Append(assembler.Flush());
                    if (!stops.IsStopped) tail += stops.Flush();
                }
                else
                {
                    assembler.Reset();
                }

                yield return new GenerationUpdate
                {
                    Text = tail,
                    Tokens = pending.ToArray(),
                    IsFinal = true,
                    FinishReason = reason,
                    Statistics = new GenerationStatistics
                    {
                        PromptTokens = promptTokens.Count,
                        CachedPromptTokens = cached,
                        GeneratedTokens = generated,
                        PromptDuration = promptDuration,
                        GenerationDuration = clock.Elapsed,
                        TotalDuration = total.Elapsed,
                    },
                };
            }
            finally
            {
                EngineSamplerChain built = Interlocked.Exchange(ref activeChain, null);
                if (built != null)
                {
                    try
                    {
                        await worker.RunAsync(built.Dispose).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        // The model was disposed under this request: the worker thread has stopped and
                        // nothing else can be holding the chain, so it is freed here instead.
                        built.Dispose();
                    }
                }
            }
        }
    }

    private void RaiseAbort()
    {
        abort.Raise();
    }

    /// <summary>
    /// Runs one decoding operation with the abort flag cleared and the caller's token wired to it, so a
    /// long native call can be stopped rather than merely being waited out.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="work">The work, which must already be inside the request gate.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The result.</returns>
    private async Task<T> AbortableAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        abort.Reset();

        using (cancellationToken.Register(RaiseAbort))
        {
            return await work().ConfigureAwait(false);
        }
    }

    private async Task<T> GatedAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task GatedAsync(Func<Task> work, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private int[] ApplyBeginningOfSequenceRule(int[] tokens, bool promptHasBos)
    {
        if (promptHasBos) return tokens;
        if (!NativeMethods.llama_vocab_get_add_bos(vocab)) return tokens;

        int bos = NativeMethods.llama_vocab_bos(vocab);
        if (bos < 0) return tokens;
        if (tokens.Length > 0 && tokens[0] == bos) return tokens;

        int[] prefixed = new int[tokens.Length + 1];
        prefixed[0] = bos;
        Array.Copy(tokens, 0, prefixed, 1, tokens.Length);
        return prefixed;
    }

    private int EvaluatePromptOnWorker(IReadOnlyList<int> promptTokens, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        EngineLog.Clear();

        if (promptTokens.Count == 0)
        {
            throw new InferenceException(
                "A completion needs at least one prompt token; the engine has nothing to condition on.");
        }

        if (promptTokens.Count > contextLength)
        {
            throw new InferenceException(
                $"The prompt is {promptTokens.Count} tokens and the context holds {contextLength}. Load the "
                + "model with a larger ContextSize, or shorten the prompt.");
        }

        int reusable = cache.ReusableLength(promptTokens);
        if (reusable < cache.Count)
        {
            // A hybrid or recurrent model keeps a rolled-up state rather than a per-token cache, so it
            // cannot drop a suffix. The engine says so by returning false, and the only correct answer is to
            // start the sequence again.
            if (NativeMethods.llama_memory_seq_rm(memory, 0, reusable, -1))
            {
                cache.TruncateTo(reusable);
            }
            else
            {
                NativeMethods.llama_memory_clear(memory, false);
                cache.Clear();
                reusable = 0;
            }
        }

        int position = reusable;
        while (position < promptTokens.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int chunk = Math.Min(batchLength, promptTokens.Count - position);
            batch.Clear();

            for (int i = 0; i < chunk; i++)
            {
                int index = position + i;

                // Only the final token of the whole prompt needs logits: it is the one the first token is
                // sampled from, and asking for the rest would allocate a row of n_vocab floats per token.
                batch.Add(promptTokens[index], index, 0, index == promptTokens.Count - 1);
            }

            DecodeOnWorker(cancellationToken);

            for (int i = 0; i < chunk; i++) cache.Add(promptTokens[position + i]);
            position += chunk;
        }

        return reusable;
    }

    private EngineStep StepOnWorker(IntPtr chain, CancellationToken cancellationToken)
    {
        // Every step of a request asks this again, because a disposal queues its own release behind
        // whatever is already queued: a step that arrived after it would read a context that is gone.
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        int token = NativeMethods.llama_sampler_sample(chain, context, -1);
        bool end = NativeMethods.llama_vocab_is_eog(vocab, token);

        // Special tokens are rendered rather than dropped, matching llama.cpp's own front ends: a template's
        // markers are part of the text a post-processor has to see, and the ones that end a turn have
        // already been caught above.
        byte[] bytes = end || !vocabularyHasTokenizer
            ? Array.Empty<byte>()
            : NativeText.TokenToBytes(vocab, token, 0, true);

        return new EngineStep(token, bytes, end);
    }

    private bool AdvanceOnWorker(int token, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (cache.Count + 1 > contextLength) return false;

        batch.Clear();
        batch.Add(token, cache.Count, 0, true);
        DecodeOnWorker(cancellationToken);
        cache.Add(token);

        return true;
    }

    private void DecodeOnWorker(CancellationToken cancellationToken)
    {
        int result = NativeMethods.llama_decode(context, batch.Batch);
        if (result == 0) return;

        if (result == 2)
        {
            // Aborted: the micro-batches that did run are still in the memory, so the record here no longer
            // describes it and the whole sequence has to go.
            ClearMemoryOnWorker();

            // A disposal is what raised the abort flag in this case, and the caller of the enumeration is
            // owed the reason it really ended rather than a report of an aborted decode.
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            throw new InferenceException(EngineLog.Describe("The engine aborted this decode."));
        }

        if (result == 1)
        {
            throw new InferenceException(EngineLog.Describe(
                "The context has no free memory slot for this batch. Load the model with a larger "
                + "ContextSize, or clear the cache between requests."));
        }

        if (result == -1)
        {
            throw new InferenceException(EngineLog.Describe(
                "The engine rejected this batch as invalid input. A token id outside the vocabulary, a "
                + "position outside the context, or a sequence id at or above MaxSequences is what it "
                + "means."));
        }

        // Anything below -1 is the engine's own fatal error, and it says nothing about what is left in the
        // context's memory, so the record of it has to go.
        ClearMemoryOnWorker();

        throw new InferenceException(EngineLog.Describe($"The engine's decode failed and returned {result}."));
    }

    private void ClearMemoryOnWorker()
    {
        NativeMethods.llama_memory_clear(memory, false);
        cache.Clear();
    }

    private unsafe float[][] DecodeForLogitsOnWorker(int[] tokens, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        EngineLog.Clear();

        if (tokens.Length == 0) return Array.Empty<float[]>();
        if (tokens.Length > batchLength)
        {
            throw new InferenceException(
                $"This hook decodes at most one batch of {batchLength} tokens at a time.");
        }

        ClearMemoryOnWorker();

        batch.Clear();
        for (int i = 0; i < tokens.Length; i++) batch.Add(tokens[i], i, 0, true);
        DecodeOnWorker(cancellationToken);
        cache.AddRange(tokens);

        int width = NativeMethods.llama_vocab_n_tokens(vocab);
        float[][] rows = new float[tokens.Length][];

        for (int position = 0; position < tokens.Length; position++)
        {
            float* logits = NativeMethods.llama_get_logits_ith(context, position);
            if (logits == null)
            {
                throw new InferenceException(EngineLog.Describe(
                    $"The engine produced no logits for position {position}."));
            }

            float[] row = new float[width];
            for (int i = 0; i < width; i++) row[i] = logits[i];
            rows[position] = row;
        }

        return rows;
    }

    private unsafe EmbeddingResult EmbedOnWorker(string[] inputs, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        EngineLog.Clear();

        IntPtr target = EnsureEmbeddingContextOnWorker();
        IntPtr targetMemory = NativeMethods.llama_get_memory(target);
        bool sharesTheGenerationContext = target == context;

        int dimensions = EmbeddingWidth(
            NativeMethods.llama_model_n_embd_out(model), NativeMethods.llama_model_n_embd(model));

        LlamaPoolingType pooling = NativeMethods.llama_pooling_type(target);
        bool encoderOnly = Details.HasEncoder && !Details.HasDecoder;

        int room = (int)NativeMethods.llama_n_ctx_seq(target);
        if (room <= 0) room = (int)NativeMethods.llama_n_ctx(target);

        int capacity = EmbeddingInputLimit(room, (int)NativeMethods.llama_n_ubatch(target));

        List<float[]> vectors = new List<float[]>(inputs.Length);
        int promptTokens = 0;

        using (LlamaBatchBuffer buffer = new LlamaBatchBuffer(capacity, 1))
        {
            foreach (string input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int[] tokens = NativeText.Tokenize(vocab, input, true, false);
                if (tokens.Length == 0)
                {
                    throw new InferenceException(
                        "An input tokenized to nothing at all; an embedding needs at least one token.");
                }

                if (tokens.Length > capacity)
                {
                    throw new InferenceException(
                        $"An input is {tokens.Length} tokens and this embedding context takes at most "
                        + $"{capacity} in one go. An embedding is computed in a single physical batch, so "
                        + "the limit is the smaller of the context size and PhysicalBatchSize; raise "
                        + "PhysicalBatchSize (and BatchSize with it) or split the text yourself.");
                }

                promptTokens += tokens.Length;

                if (targetMemory != IntPtr.Zero) NativeMethods.llama_memory_clear(targetMemory, false);
                if (sharesTheGenerationContext) cache.Clear();

                buffer.Clear();
                for (int i = 0; i < tokens.Length; i++) buffer.Add(tokens[i], i, 0, true);

                int result = encoderOnly
                    ? NativeMethods.llama_encode(target, buffer.Batch)
                    : NativeMethods.llama_decode(target, buffer.Batch);

                if (result != 0)
                {
                    if (targetMemory != IntPtr.Zero) NativeMethods.llama_memory_clear(targetMemory, false);
                    if (sharesTheGenerationContext) cache.Clear();

                    cancellationToken.ThrowIfCancellationRequested();

                    throw new InferenceException(EngineLog.Describe(
                        $"The engine could not embed an input; it returned {result}."));
                }

                float* data = pooling == LlamaPoolingType.None
                    ? NativeMethods.llama_get_embeddings_ith(target, tokens.Length - 1)
                    : NativeMethods.llama_get_embeddings_seq(target, 0);

                if (data == null)
                {
                    throw new InferenceException(EngineLog.Describe(
                        "This model produced no embeddings. A model with no embedding output cannot be asked "
                        + "for one."));
                }

                float[] vector = new float[dimensions];
                for (int i = 0; i < dimensions; i++) vector[i] = data[i];
                vectors.Add(vector);
            }
        }

        return new EmbeddingResult
        {
            Embeddings = vectors,
            Dimensions = dimensions,
            PromptTokens = promptTokens,
        };
    }

    private unsafe IntPtr EnsureEmbeddingContextOnWorker()
    {
        if (Options.EmbeddingsMode) return context;
        if (embeddingContext != IntPtr.Zero) return embeddingContext;

        // A generative model can embed, but only from a context created for it, and re-creating the loaded
        // one would throw away the conversation in its memory. A second, small context on the same weights
        // costs a cache the size of a couple of thousand tokens and leaves generation untouched.
        int trained = Details.TrainingContextLength;
        uint wanted = (uint)Math.Max(256, Math.Min(trained <= 0 ? 2048 : trained, 2048));

        // The physical batch is made as wide as the logical one on purpose: an encoder, and any model whose
        // attention is not causal, has to see the whole input in one micro-batch or the engine asserts and
        // ends the process. A context of this size costs nothing next to the weights.
        LlamaContextParams parameters = ParameterMapper.BuildContextParams(Options, abort, true, wanted);
        parameters.NBatch = wanted;
        parameters.NUbatch = wanted;

        IntPtr created = NativeMethods.llama_init_from_model(model, parameters);
        if (created == IntPtr.Zero)
        {
            throw new InferenceException(EngineLog.Describe(
                "The engine could not create an embeddings context for this model."));
        }

        embeddingContextHandle = new SafeLlamaContextHandle(created);
        embeddingContext = created;
        return embeddingContext;
    }

    private unsafe void ApplyAdaptersOnWorker(IReadOnlyList<LoraAdapterOptions> wanted)
    {
        ThrowIfDisposed();

        int count = wanted.Count;
        IntPtr[] handles = new IntPtr[Math.Max(1, count)];
        float[] scales = new float[Math.Max(1, count)];

        for (int i = 0; i < count; i++)
        {
            LoraAdapterOptions adapter = wanted[i];
            if (adapter == null || string.IsNullOrWhiteSpace(adapter.Path))
            {
                throw new ArgumentException($"Every {nameof(LoraAdapterOptions)} must name a file.", nameof(wanted));
            }

            string key = Path.GetFullPath(adapter.Path);
            SafeLlamaAdapterHandle loaded;

            if (!adapters.TryGetValue(key, out loaded))
            {
                IntPtr raw = NativeMethods.llama_adapter_lora_init(model, key);
                if (raw == IntPtr.Zero)
                {
                    throw new ModelLoadException(EngineLog.Describe(
                        $"The engine could not load the LoRA adapter at '{key}'."));
                }

                loaded = new SafeLlamaAdapterHandle(raw);
                adapters[key] = loaded;
            }

            handles[i] = loaded.DangerousGetHandle();
            scales[i] = adapter.Scale;
        }

        int result;
        fixed (IntPtr* handlePointer = handles)
        fixed (float* scalePointer = scales)
        {
            result = NativeMethods.llama_set_adapters_lora(context, handlePointer, (nuint)count, scalePointer);
        }

        if (result != 0)
        {
            throw new ModelLoadException(EngineLog.Describe(
                $"The engine would not apply {count} LoRA adapter(s); it returned {result}."));
        }
    }

    private void ReleaseOnWorker()
    {
        // Before the context: a chain left over from a request this disposal interrupted is freed here, and
        // freeing it is the only thing that returns its grammar's memory.
        EngineSamplerChain chain = Interlocked.Exchange(ref activeChain, null);
        if (chain != null) chain.Dispose();

        LlamaBatchBuffer buffer = batch;
        batch = null;
        if (buffer != null) buffer.Dispose();

        SafeLlamaContextHandle embedding = embeddingContextHandle;
        embeddingContextHandle = null;
        embeddingContext = IntPtr.Zero;
        if (embedding != null) embedding.Dispose();

        SafeLlamaContextHandle running = contextHandle;
        contextHandle = null;
        if (running != null) running.Dispose();

        // Adapters belong to the model and would be freed with it, but freeing them here keeps the order
        // deterministic and releases their weights before the model's.
        foreach (SafeLlamaAdapterHandle adapter in adapters.Values) adapter.Dispose();
        adapters.Clear();

        SafeLlamaModelHandle weights = modelHandle;
        modelHandle = null;
        if (weights != null) weights.Dispose();

        cache.Clear();

        // Only once no context can still be computing: a compute thread reads this on every graph node.
        abort.Dispose();
    }

    /// <summary>Refuses the text calls of a model whose file carries no tokenizer.</summary>
    /// <param name="member">The member being called, for the message.</param>
    /// <exception cref="InferenceException">The vocabulary has no tokenizer.</exception>
    private void ThrowIfNoTokenizer(string member)
    {
        if (vocabularyHasTokenizer) return;

        throw new InferenceException(
            $"{nameof(IRunningModel)}.{member} needs a tokenizer and this model's file carries none: its "
            + "vocabulary type is 'none', which only a model meant to be fed token ids has. The engine "
            + "would end the process rather than answer.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }

    /// <summary>
    /// Refuses a call made from inside an <c>await foreach</c> over this same model rather than waiting on a
    /// gate only that same call chain could release.
    /// </summary>
    /// <param name="member">The member being called, for the message.</param>
    /// <exception cref="InvalidOperationException">This chain is already streaming from this model.</exception>
    private void ThrowIfStreaming(string member)
    {
        EngineRequestScope current = Scope.Value;
        if (current == null || !ReferenceEquals(current.Model, this) || !current.StreamHoldsTheGate) return;

        throw new InvalidOperationException(
            $"{nameof(IRunningModel)}.{member} cannot be called while an enumeration of this model is in "
            + "progress on the same call chain; finish or dispose the enumeration first.");
    }

    /// <summary>
    /// Marks this call chain as streaming from this model and hands back the marker the enumeration flips.
    /// </summary>
    /// <returns>The marker, which is this chain's existing one when it already has one for this model.</returns>
    private EngineRequestScope BeginStream()
    {
        EngineRequestScope current = Scope.Value;
        if (current != null && ReferenceEquals(current.Model, this))
        {
            // Reuse the caller-visible marker. The gate refuses overlapping enumerations when they start;
            // merely constructing a second enumerable does not change the active one's ownership.
            return current;
        }

        EngineRequestScope scope = new EngineRequestScope(this);
        Scope.Value = scope;
        return scope;
    }

    /// <summary>Whether this call chain is the one holding this model's request gate.</summary>
    private bool StreamingOnThisChain
    {
        get
        {
            EngineRequestScope current = Scope.Value;
            return current != null && ReferenceEquals(current.Model, this) && current.StreamHoldsTheGate;
        }
    }
}

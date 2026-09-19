using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The loaded text-generation model: the graph, the tokenizer the bundle carries, and the loop that drives
/// them, presented as the library's own <see cref="IRunningModel"/>.
/// </summary>
/// <remarks>
/// It owns the graph and disposing it releases the weights. Nothing is carried from one generation to the
/// next - the cache belongs to the generation - so the same loaded model answers one prompt after another and
/// the weights are read once for all of them.
/// </remarks>
internal sealed class CausalLmGenerationModel : IOnnxCausalLmModel
{
    private readonly IOnnxModel _model;
    private readonly CausalLmBundle _bundle;
    private readonly Gpt2ByteLevelTokenizer _tokenizer;
    private readonly CausalLmGenerator _generator;
    private int _generating;
    private int _disposed;

    /// <summary>Creates the model over a loaded graph.</summary>
    /// <param name="bundle">What the bundle said about itself.</param>
    /// <param name="tokenizer">The tokenizer the bundle carries.</param>
    /// <param name="model">The loaded graph.</param>
    internal CausalLmGenerationModel(
        CausalLmBundle bundle, Gpt2ByteLevelTokenizer tokenizer, IOnnxModel model)
    {
        _bundle = bundle;
        _tokenizer = tokenizer;
        _model = model;
        _generator = new CausalLmGenerator(model, bundle, tokenizer);

        Metadata = new CausalLmMetadata(
            bundle.Architecture,
            bundle.Decoder.FileName,
            Gpt2TokenizerFiles.Kind,
            bundle.Decoder.LayerCount,
            bundle.Decoder.HeadCount,
            bundle.Decoder.KeyValueHeadCount,
            bundle.Decoder.HeadSize,
            bundle.Decoder.HiddenSize,
            bundle.ContextLength,
            bundle.VocabularySize,
            bundle.BeginningOfSequence,
            bundle.EndOfSequence,
            bundle.Padding,
            tokenizer.MergeCount);

        Details = BuildDetails(bundle, model);
        Options = BuildOptions(bundle, model);
    }

    /// <inheritdoc />
    public CausalLmMetadata Metadata { get; }

    /// <inheritdoc />
    public OnnxRunnerOptions RunnerOptions => _model.Options;

    /// <summary>
    /// What this library knows about the loaded bundle, in the shape the rest of the library reports a model
    /// in. <c>Path</c> is the graph file, <c>FileSize</c> and <c>WeightsSize</c> are what the bundle's graph
    /// and its side files occupy on disk, and <c>ParameterCount</c> is nought because a generation
    /// configuration does not state one. <see cref="Metadata"/> is the fuller answer.
    /// </summary>
    public ModelDetails Details { get; }

    /// <summary>
    /// The few load settings the shared contract has a place for: the graph's path, the thread count in use
    /// and the model's context length. Everything else on it is a checkpoint-loading setting that an ONNX
    /// bundle has no equivalent of; <see cref="RunnerOptions"/> is what this model was really loaded with.
    /// </summary>
    public ModelRunnerOptions Options { get; }

    /// <summary>
    /// Always <see cref="ChatTemplateDialect.Auto"/>, and nothing renders a chat template on this route: the
    /// three chat members are refused by name. It is here because the shared contract states it.
    /// </summary>
    public ChatTemplateDialect ChatTemplateDialect => ChatTemplateDialect.Auto;

    /// <inheritdoc />
    public Task<IReadOnlyList<int>> TokenizeAsync(
        string text,
        bool addSpecialTokens = true,
        bool parseSpecialTokens = true,
        CancellationToken cancellationToken = default)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        RequireOpen();

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_tokenizer.Encode(text, addSpecialTokens, parseSpecialTokens));
    }

    /// <inheritdoc />
    public Task<string> DetokenizeAsync(
        IReadOnlyList<int> tokens,
        bool renderSpecialTokens = false,
        CancellationToken cancellationToken = default)
    {
        if (tokens == null) throw new ArgumentNullException(nameof(tokens));
        RequireOpen();

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_tokenizer.Decode(tokens, renderSpecialTokens));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<GenerationUpdate> GenerateAsync(
        string prompt, GenerationOptions options = null, CancellationToken cancellationToken = default)
    {
        if (prompt == null) throw new ArgumentNullException(nameof(prompt));
        RequireOpen();

        //Settled BEFORE the enumeration begins, so that a setting this driver does not implement is refused
        //where the caller wrote it rather than at the first step of the loop.
        CausalLmPlan plan = CausalLmPlan.For(options);
        IReadOnlyList<int> tokens = Prompt(prompt);
        _generator.RequireRunnable(tokens);

        return Stream(tokens, plan, cancellationToken);
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

    /// <summary>Refused: a bundle carries no chat template and this driver applies none.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Nothing; the call throws.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public IAsyncEnumerable<ChatUpdate> ChatAsync(
        ChatRequest request, CancellationToken cancellationToken = default) => throw Chat(nameof(ChatAsync));

    /// <summary>Refused: a bundle carries no chat template and this driver applies none.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Nothing; the call throws.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Task<ChatResponse> ChatToEndAsync(
        ChatRequest request, CancellationToken cancellationToken = default) =>
        throw Chat(nameof(ChatToEndAsync));

    /// <summary>Refused: a bundle carries no chat template and this driver applies none.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Nothing; the call throws.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Task<string> RenderChatPromptAsync(
        ChatRequest request, CancellationToken cancellationToken = default) =>
        throw Chat(nameof(RenderChatPromptAsync));

    /// <summary>Refused: a decoder graph answers with scores over the vocabulary and no hidden state to pool.</summary>
    /// <param name="inputs">The texts.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Nothing; the call throws.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Task<EmbeddingResult> EmbedAsync(
        IReadOnlyList<string> inputs, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "EmbedAsync is not supported when generating from an ONNX bundle: the graph a text-generation"
            + " bundle carries answers with one score per token of the vocabulary and hands back no hidden"
            + " state to pool into a vector.");

    /// <summary>Refused: an adapter is applied to a checkpoint's weights, and a graph has none to apply it to.</summary>
    /// <param name="adapters">The adapters and their scales.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Nothing; the call throws.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Task SetLoraAdaptersAsync(
        IReadOnlyList<LoraAdapterOptions> adapters, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "SetLoraAdaptersAsync is not supported when generating from an ONNX bundle: an adapter is applied"
            + " to a checkpoint's weights as it is loaded, and a graph's weights are already baked into it.");

    /// <summary>
    /// Completes at once. Every request on this route evaluates its whole prompt already - the cache belongs
    /// to the generation and is emptied when it ends - so there is nothing held between requests to clear.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A completed task.</returns>
    public Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        RequireOpen();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <summary>Releases the graph's weights.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _model.Dispose();
    }

    /// <summary>Releases the graph's weights.</summary>
    /// <returns>A completed task; there is no I/O to wait for.</returns>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private static NotSupportedException Chat(string member) =>
        new NotSupportedException(
            member + " is not supported when generating from an ONNX bundle: a bundle carries no chat"
            + " template and this driver applies none, so a conversation is the caller's to render into a"
            + " prompt and pass to GenerateAsync.");

    private static ModelDetails BuildDetails(CausalLmBundle bundle, IOnnxModel model)
    {
        string path = bundle.FindFile(bundle.Decoder.FileName);
        long size = Length(path);
        long weights = size;
        foreach (KeyValuePair<string, string> file in bundle.Files)
        {
            if (!file.Key.EndsWith(".onnx.data", StringComparison.OrdinalIgnoreCase)) continue;
            weights += Length(file.Value);
        }

        Dictionary<string, string> metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["onnx.model_type"] = bundle.Architecture,
            ["onnx.graph_file"] = bundle.Decoder.FileName,
            ["onnx.producer"] = model.Metadata.ProducerName ?? "(none stated)",
            ["onnx.tokenizer"] = Gpt2TokenizerFiles.Kind,
        };

        return new ModelDetails
        {
            Path = path,
            FileSize = size,
            Description = bundle.Architecture + ", "
                + bundle.Decoder.LayerCount.ToString(CultureInfo.InvariantCulture) + " layers, "
                + bundle.Decoder.HeadCount.ToString(CultureInfo.InvariantCulture) + " heads, ONNX bundle",
            Architecture = bundle.Architecture,
            Name = null,
            ParameterCount = 0,
            WeightsSize = (ulong)weights,
            TrainingContextLength = bundle.ContextLength,
            EmbeddingLength = bundle.Decoder.HiddenSize < 0 ? 0 : bundle.Decoder.HiddenSize,
            LayerCount = bundle.Decoder.LayerCount,
            HeadCount = bundle.Decoder.HeadCount,
            VocabularySize = bundle.VocabularySize,
            IsHybrid = false,
            IsRecurrent = false,
            HasEncoder = false,
            HasDecoder = true,
            ChatTemplate = null,
            Metadata = metadata,
        };
    }

    private static ModelRunnerOptions BuildOptions(CausalLmBundle bundle, IOnnxModel model) =>
        new ModelRunnerOptions
        {
            ModelPath = bundle.FindFile(bundle.Decoder.FileName),
            Threads = model.Options.Threads,
            ContextSize = (uint)bundle.ContextLength,
        };

    private static long Length(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;

        try
        {
            FileInfo file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private IReadOnlyList<int> Prompt(string prompt)
    {
        IReadOnlyList<int> tokens = _tokenizer.Encode(prompt, true, true);
        if (tokens.Count > 0 || _bundle.BeginningOfSequence < 0) return tokens;

        //An empty prompt has nothing to read, so the model is started from the token the bundle says a
        //sequence begins with - which is what generating "from nothing" means for a model of this kind.
        return new[] { _bundle.BeginningOfSequence };
    }

    private async IAsyncEnumerable<GenerationUpdate> Stream(
        IReadOnlyList<int> prompt,
        CausalLmPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RequireOpen();
        if (Interlocked.Exchange(ref _generating, 1) != 0)
        {
            throw new InferenceException(
                "This model is already generating. A generation runs one step at a time, so a second one is"
                + " refused rather than queued; load the bundle again to generate two at once.");
        }

        try
        {
            await foreach (GenerationUpdate update in _generator
                .GenerateAsync(prompt, plan, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return update;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _generating, 0);
        }
    }

    private void RequireOpen()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(IOnnxCausalLmModel));
        }
    }
}

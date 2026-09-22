using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The loaded MIDI-generating model: the two graphs, the tokenizer they were trained in, and the loop that
/// drives them.
/// </summary>
/// <remarks>
/// It owns both graphs and disposing it releases both. Nothing is carried from one generation to the next -
/// the caches belong to the generation - so the same loaded model writes one piece after another, and the
/// weights are read once for all of them.
/// </remarks>
internal sealed class SkyTntGenerationModel : IMidiGenerationModel
{
    private readonly IOnnxModel _base;
    private readonly IOnnxModel _token;
    private readonly SkyTntTokenizer _tokenizer;
    private readonly SkyTntGenerator _generator;
    private int _generating;
    private int _disposed;

    /// <summary>Creates the model over two loaded graphs.</summary>
    /// <param name="bundle">What the bundle said about itself.</param>
    /// <param name="tokenizer">The tokenizer built from it and checked against it.</param>
    /// <param name="baseModel">The graph that turns events into hidden states.</param>
    /// <param name="tokenModel">The graph that turns a hidden state into an event's tokens.</param>
    internal SkyTntGenerationModel(
        SkyTntBundle bundle, SkyTntTokenizer tokenizer, IOnnxModel baseModel, IOnnxModel tokenModel)
    {
        _base = baseModel;
        _token = tokenModel;
        _tokenizer = tokenizer;
        _generator = new SkyTntGenerator(baseModel, tokenModel, tokenizer);

        Metadata = new MidiGenerationMetadata(
            bundle.Architecture,
            tokenizer.OptimiseMidi ? SkyTntTokenizer.SupportedVersion + " (optimised)" : bundle.Tokenizer.Version,
            SkyTntTokenizer.TicksPerQuarterNote,
            SkyTntGenerator.MaximumContextEvents,
            tokenizer.VocabularySize,
            tokenizer.MaximumTokensPerEvent,
            bundle.BaseGraph,
            bundle.TokenGraph);
    }

    /// <inheritdoc />
    public MidiGenerationMetadata Metadata { get; }

    /// <inheritdoc />
    public OnnxRunnerOptions Options => _base.Options;

    /// <inheritdoc />
    public IAsyncEnumerable<MidiEvent> GenerateAsync(
        MidiGenerationOptions options = null, CancellationToken cancellationToken = default)
    {
        RequireOpen();

        //Settled BEFORE the enumeration begins, so that an option outside its range is refused where the
        //caller wrote it rather than at the first step of the loop.
        SkyTntGenerationPlan plan = SkyTntPrompt.Plan(
            _tokenizer, (options ?? new MidiGenerationOptions()).Copy());
        return Stream(plan, cancellationToken);
    }

    /// <inheritdoc />
    public MidiScore ToScore(IEnumerable<MidiEvent> events)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));
        return _tokenizer.Tidy(events);
    }

    /// <inheritdoc />
    public Task SaveAsync(
        string path, IEnumerable<MidiEvent> events, CancellationToken cancellationToken = default)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));
        return MidiFile.WriteAsync(path, ToScore(events), cancellationToken);
    }

    /// <summary>Releases both graphs' weights.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _base.Dispose();
        _token.Dispose();
    }

    /// <summary>Releases both graphs' weights.</summary>
    /// <returns>A completed task; there is no I/O to wait for.</returns>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async IAsyncEnumerable<MidiEvent> Stream(
        SkyTntGenerationPlan plan, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RequireOpen();
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _generating, 1) != 0)
        {
            throw new InferenceException(
                "This model is already writing a piece. A generation runs one step at a time, so a second one"
                + " is refused rather than queued; load the model again to write two pieces at once.");
        }

        try
        {
            await foreach (MidiEvent item in _generator
                .GenerateAsync(plan, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return item;
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
            throw new ObjectDisposedException(nameof(IMidiGenerationModel));
        }
    }
}

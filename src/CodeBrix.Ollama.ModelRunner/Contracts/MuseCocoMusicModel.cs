using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>MuseCoco attribute-to-MIDI generation with a portable managed recurrent ONNX decoder.</summary>
/// <remarks>
/// Each request begins with empty attention state; instances do not retain a prompt cache between requests.
/// BERT is optional. Use attributes from this model's schema or a compatible text model's prediction.
/// Each instance permits one generation at a time. No Python or additional inference runtime is required.
/// </remarks>
public sealed class MuseCocoMusicModel : IDisposable, IAsyncDisposable
{
    private readonly IOnnxModel _graph;
    private readonly string[] _vocabulary;
    private readonly Dictionary<string, int> _tokenIds;
    private readonly int _layers;
    private readonly int _heads;
    private readonly int _headSize;
    private readonly int _prefixPositionStart;
    private readonly int _eos;
    private readonly int _pad;
    private int _state;

    private MuseCocoMusicModel(IOnnxModel graph, MuseCocoBundle bundle, string[] vocabulary)
    {
        _graph = graph;
        _vocabulary = vocabulary;
        Schema = bundle.Schema;
        _tokenIds = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < vocabulary.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(vocabulary[i]) || !_tokenIds.TryAdd(vocabulary[i], i))
            {
                throw new ModelLoadException("Invalid MuseCoco vocabulary.");
            }
        }
        if (vocabulary.Length < 5 || !_tokenIds.TryGetValue("</s>", out _eos)
            || !_tokenIds.TryGetValue("<pad>", out _pad) || !_tokenIds.ContainsKey("<sep>") || _pad != 1)
        {
            throw new ModelLoadException("The MuseCoco vocabulary lacks the required special tokens.");
        }
        foreach (MusicAttributeDefinition definition in Schema.Definitions)
        {
            if (definition.Tokens.Any(t => !_tokenIds.ContainsKey(t)))
            {
                throw new ModelLoadException("An attribute token is missing from the music vocabulary: " + definition.Name);
            }
        }
        _layers = MuseCocoBundle.Positive(bundle.Metadata, "layers", 128);
        _heads = MuseCocoBundle.Positive(bundle.Metadata, "heads", 256);
        _headSize = MuseCocoBundle.Positive(bundle.Metadata, "headSize", 512);
        MaximumGenerationTokens = MuseCocoBundle.Positive(bundle.Metadata, "positionCount", 65536) - 3;
        _prefixPositionStart = MuseCocoBundle.Positive(bundle.Metadata, "prefixPositionStart", 128);
        if (_prefixPositionStart != Schema.Definitions.Count || MaximumGenerationTokens < 1
            || bundle.Metadata.GetProperty("ticksPerQuarterNote").GetInt32() != 480
            || bundle.Metadata.GetProperty("positionsPerQuarterNote").GetInt32() != 12)
        {
            throw new ModelLoadException("Unsupported MuseCoco position or MIDI decoding contract.");
        }
        MuseCocoBundle.RequireTensor(graph.Metadata.Inputs, "token", OnnxElementType.Int64, 1);
        MuseCocoBundle.RequireTensor(graph.Metadata.Inputs, "position", OnnxElementType.Int64, 1);
        MuseCocoBundle.RequireTensor(graph.Metadata.Outputs, "logits", OnnxElementType.Float, 1, vocabulary.Length);
        for (int i = 0; i < _layers; i++)
        {
            MuseCocoBundle.RequireTensor(graph.Metadata.Inputs, "state_s_" + i, OnnxElementType.Float, _heads, _headSize, _headSize);
            MuseCocoBundle.RequireTensor(graph.Metadata.Inputs, "state_z_" + i, OnnxElementType.Float, _heads, _headSize);
            MuseCocoBundle.RequireTensor(graph.Metadata.Outputs, "next_s_" + i, OnnxElementType.Float, _heads, _headSize, _headSize);
            MuseCocoBundle.RequireTensor(graph.Metadata.Outputs, "next_z_" + i, OnnxElementType.Float, _heads, _headSize);
        }
        if (graph.Metadata.Inputs.Count != 2 + _layers * 2 || graph.Metadata.Outputs.Count != 1 + _layers * 2)
        {
            throw new ModelLoadException("Unexpected tensors in the recurrent music graph contract.");
        }
    }

    /// <summary>Attribute names, values and defaults accepted by this music model.</summary>
    public MusicAttributeSchema Schema { get; }

    /// <summary>Maximum generated token count allowed by the exported position table.</summary>
    public int MaximumGenerationTokens { get; }

    /// <summary>The recurrent graph's metadata.</summary>
    public OnnxModelMetadata Metadata => _graph.Metadata;

    /// <summary>The execution settings used by the loaded graph.</summary>
    public OnnxRunnerOptions Options => _graph.Options;

    /// <summary>Loads a staged music bundle from a directory. Dispose the returned model to release its weights.</summary>
    public static async Task<MuseCocoMusicModel> LoadFromDirectoryAsync(
        string directory, OnnxRunnerOptions options = null, CancellationToken cancellationToken = default)
    {
        MuseCocoBundle bundle = await MuseCocoBundle.FromDirectoryAsync(directory, "music", cancellationToken).ConfigureAwait(false);
        return await LoadAsync(bundle, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads a staged music bundle from logical filename to physical path pairs, without copying store blobs.</summary>
    public static async Task<MuseCocoMusicModel> LoadFromFilesAsync(
        IReadOnlyDictionary<string, string> files, OnnxRunnerOptions options = null, CancellationToken cancellationToken = default)
    {
        MuseCocoBundle bundle = await MuseCocoBundle.FromFilesAsync(files, "music", cancellationToken).ConfigureAwait(false);
        return await LoadAsync(bundle, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<MuseCocoMusicModel> LoadAsync(MuseCocoBundle bundle, OnnxRunnerOptions options, CancellationToken cancellationToken)
    {
        IOnnxModel graph = null;
        try
        {
            var vocabulary = await bundle.ReadJsonAsync(MuseCocoBundle.String(bundle.Metadata, "vocabulary"), cancellationToken).ConfigureAwait(false);
            string[] words = vocabulary.EnumerateArray().Select(v => v.GetString()).ToArray();
            graph = await bundle.LoadGraphAsync(options, cancellationToken).ConfigureAwait(false);
            return new MuseCocoMusicModel(graph, bundle, words);
        }
        catch (Exception error)
        {
            if (graph != null) await graph.DisposeAsync().ConfigureAwait(false);
            if (error is System.Text.Json.JsonException || error is KeyNotFoundException
                || error is InvalidOperationException || error is FormatException || error is OverflowException)
                throw new ModelLoadException("Invalid MuseCoco music bundle metadata.", error);
            throw;
        }
    }

    /// <summary>
    /// Generates a score from attributes. Null attributes use every schema default. The result contains
    /// raw tokens, the effective seed and a MIDI score; incomplete endings follow the publisher's cleanup.
    /// Cancellation discards the partial result and releases the instance for its next request.
    /// </summary>
    public async Task<MuseCocoGenerationResult> GenerateAsync(
        MusicAttributes attributes = null, MuseCocoGenerationOptions options = null,
        IProgress<int> progress = null, CancellationToken cancellationToken = default)
    {
        attributes ??= Schema.CreateAttributes();
        MuseCocoGenerationOptions effective = BeginGeneration(attributes, options);
        try
        {
            var state = new MuseCocoGenerationState();
            var generated = new List<int>();
            await foreach (int token in GenerateTokensAsync(attributes, effective, state, progress, cancellationToken)
                .ConfigureAwait(false))
            {
                generated.Add(token);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new MuseCocoGenerationResult(DecodeTokens(generated), generated.ToArray(), state.Seed,
                attributes, state.EndedWithEos, state.PromptTime, state.GenerationTime);
        }
        finally
        {
            Volatile.Write(ref _state, 0);
        }
    }

    /// <summary>
    /// Streams MIDI events from attributes while later bars are still being generated.
    /// Events use 480 ticks per quarter note and are released in timestamp order after each completed bar.
    /// </summary>
    /// <param name="attributes">Attributes from this model's schema, or null for its defaults.</param>
    /// <param name="options">Sampling and token limits, or null for their defaults.</param>
    /// <param name="progress">Optional generated-token counts, including tokens in the buffered bar.</param>
    /// <param name="cancellationToken">Stops generation and discards events not yet yielded.</param>
    /// <returns>An async stream of notes, program changes, tempo changes and time signatures.</returns>
    /// <remarks>
    /// Enumeration starts the request and holds the instance until the enumeration completes or is disposed.
    /// Dispose the enumerator when stopping early. Cancellation preserves events already handed to the caller.
    /// The unfinished final bar receives the same note cleanup as GenerateAsync. Channels and tracks are
    /// assigned as instruments first become playable, with percussion on channel 9; assignments stay fixed.
    /// Program changes precede each instrument's first note at that note's tick. This can differ from the
    /// channel/track numbering and tick-zero program changes in a completed GenerateAsync score.
    /// Events carry HorizonTicks equal to Tick: later events may share that tick but cannot precede it.
    /// Playback should consume events strictly before the horizon, then drain its queue when the stream ends.
    /// A stream establishes default 120 BPM and 4/4 at tick zero when no explicit initial values were given.
    /// </remarks>
    public async IAsyncEnumerable<MidiEvent> GenerateStreamingAsync(
        MusicAttributes attributes = null, MuseCocoGenerationOptions options = null,
        IProgress<int> progress = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        attributes ??= Schema.CreateAttributes();
        MuseCocoGenerationOptions effective = BeginGeneration(attributes, options);
        try
        {
            var decoder = new Remigen2StreamDecoder();
            var state = new MuseCocoGenerationState();
            await foreach (int token in GenerateTokensAsync(attributes, effective, state, progress, cancellationToken)
                .ConfigureAwait(false))
            {
                foreach (MidiEvent item in decoder.Add(_vocabulary[token]))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach (MidiEvent item in decoder.Complete())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
        finally
        {
            Volatile.Write(ref _state, 0);
        }
    }

    private MuseCocoGenerationOptions BeginGeneration(MusicAttributes attributes, MuseCocoGenerationOptions options)
    {
        if (!Schema.IsCompatibleWith(attributes.Schema))
            throw new ArgumentException("The attributes belong to an incompatible model schema.", nameof(attributes));
        MuseCocoGenerationOptions effective = (options ?? new MuseCocoGenerationOptions()).CopyAndValidate(MaximumGenerationTokens);
        int previous = Interlocked.CompareExchange(ref _state, 1, 0);
        if (previous == 2) throw new ObjectDisposedException(nameof(MuseCocoMusicModel));
        if (previous != 0) throw new InferenceException("A generation is already running on this MuseCoco music model.");
        return effective;
    }

    private async IAsyncEnumerable<int> GenerateTokensAsync(
        MusicAttributes attributes, MuseCocoGenerationOptions effective, MuseCocoGenerationState state,
        IProgress<int> progress, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state.Seed = effective.Seed ?? FreshSeed();
        var random = new GenerationRandom(state.Seed);
        int[] prefix = BuildPrefix(attributes);
        var inputs = new Dictionary<string, OnnxTensor>(StringComparer.Ordinal);
        OnnxSession reusable = Options.ReuseBuffers ? _graph as OnnxSession : null;
        var firstState = new Dictionary<string, OnnxTensor>(StringComparer.Ordinal);
        var secondState = new Dictionary<string, OnnxTensor>(StringComparer.Ordinal);
        for (int i = 0; i < _layers; i++)
        {
            inputs["state_s_" + i] = OnnxTensor.FromFloats(new float[checked(_heads * _headSize * _headSize)], _heads, _headSize, _headSize);
            inputs["state_z_" + i] = OnnxTensor.FromFloats(new float[checked(_heads * _headSize)], _heads, _headSize);
            if (reusable != null)
            {
                firstState["next_s_" + i] = inputs["state_s_" + i];
                firstState["next_z_" + i] = inputs["state_z_" + i];
                secondState["next_s_" + i] = OnnxTensor.FromFloats(new float[checked(_heads * _headSize * _headSize)], _heads, _headSize, _headSize);
                secondState["next_z_" + i] = OnnxTensor.FromFloats(new float[checked(_heads * _headSize)], _heads, _headSize);
            }
        }
        Dictionary<string, OnnxTensor> destination = secondState;
        IReadOnlyDictionary<string, OnnxTensor> output = null;
        async Task StepAsync(int token, int position)
        {
            inputs["token"] = OnnxTensor.FromInt64(new long[] { token }, 1);
            inputs["position"] = OnnxTensor.FromInt64(new long[] { position }, 1);
            output = reusable == null
                ? await _graph.RunAsync(inputs, cancellationToken).ConfigureAwait(false)
                : await reusable.RunIntoAsync(inputs, destination, cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < _layers; i++)
            {
                inputs["state_s_" + i] = output["next_s_" + i];
                inputs["state_z_" + i] = output["next_z_" + i];
            }
            destination = ReferenceEquals(destination, secondState) ? firstState : secondState;
        }
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < prefix.Length; i++)
        {
            // Preserve the publisher's leading-EOS convention: its separator-position offset
            // starts positional embeddings at the last attribute, one token before <sep>.
            await StepAsync(prefix[i], i < _prefixPositionStart ? 0 : i - _prefixPositionStart + 2).ConfigureAwait(false);
        }
        state.PromptTime = timer.Elapsed;
        timer.Restart();
        for (int i = 0; i < effective.MaximumTokens; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[] logits = output["logits"].Floats;
            if (logits == null || logits.Length != _vocabulary.Length)
                throw new InferenceException("The decoder returned invalid logits.");
            int token = MuseCocoSampler.Sample(logits, _pad, _eos, i >= effective.MinimumTokens, effective, random);
            if (token == _eos)
            {
                state.EndedWithEos = true;
                break;
            }
            progress?.Report(i + 1);
            // Consumer playback and MIDI decoding are not inference time.
            timer.Stop();
            yield return token;
            timer.Start();
            cancellationToken.ThrowIfCancellationRequested();
            if (i + 1 < effective.MaximumTokens)
                await StepAsync(token, prefix.Length + i - _prefixPositionStart + 2).ConfigureAwait(false);
        }
        state.GenerationTime = timer.Elapsed;
    }

    /// <summary>Decodes saved token IDs from this model into MIDI events without inference or I/O.</summary>
    public MidiScore DecodeTokens(IReadOnlyList<int> tokenIds)
    {
        if (tokenIds == null) throw new ArgumentNullException(nameof(tokenIds));
        return Remigen2Decoder.Decode(tokenIds, _vocabulary);
    }

    internal int[] BuildPrefix(MusicAttributes attributes)
    {
        var prefix = new int[Schema.Definitions.Count + 2];
        prefix[0] = _eos;
        for (int i = 0; i < Schema.Definitions.Count; i++) prefix[i + 1] = _tokenIds[Schema.Definitions[i].Tokens[attributes.IndexAt(i)]];
        prefix[prefix.Length - 1] = _tokenIds["<sep>"];
        return prefix;
    }

    private static long FreshSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        long seed;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            seed = BitConverter.ToInt64(bytes) & long.MaxValue;
        } while (seed == uint.MaxValue);
        return seed;
    }

    /// <summary>
    /// Releases weights. Finish or dispose an active event enumeration, or cancel and await an active
    /// completed-score request, before disposing the model.
    /// </summary>
    public void Dispose()
    {
        int previous = Interlocked.CompareExchange(ref _state, 2, 0);
        if (previous == 1) throw new InvalidOperationException("Finish the active generation or dispose its event enumerator before disposing the model.");
        if (previous == 0) _graph.Dispose();
    }

    /// <summary>Releases weights; equivalent to <see cref="Dispose"/> because disposal performs no I/O.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

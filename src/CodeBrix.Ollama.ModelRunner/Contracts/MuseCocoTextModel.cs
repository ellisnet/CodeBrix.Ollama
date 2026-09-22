using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>MuseCoco's optional BERT text-to-music-attribute model, executed entirely in managed .NET.</summary>
/// <remarks>
/// Loads FP32, INT8 or INT4 bundles with the same contract. Each instance permits one prediction at a
/// time. Prediction is deterministic; the music model owns sampling and seeds. No Python is needed.
/// </remarks>
public sealed class MuseCocoTextModel : IDisposable, IAsyncDisposable
{
    private readonly IOnnxModel _graph;
    private readonly BertWordPieceTokenizer _tokenizer;
    private readonly MusicAttributeDefinition[] _classifiers;
    private int _state;

    private MuseCocoTextModel(IOnnxModel graph, MuseCocoBundle bundle, BertWordPieceTokenizer tokenizer)
    {
        _graph = graph;
        _tokenizer = tokenizer;
        Schema = bundle.Schema;
        MaximumSequenceLength = MuseCocoBundle.Positive(bundle.Metadata, "maxSequenceLength", 512);
        if (MaximumSequenceLength < 2) throw new ModelLoadException("BERT requires space for CLS and SEP.");
        _classifiers = Schema.Definitions.Where(d => d.PredictedFromText).OrderBy(d => d.Classifier).ToArray();
        if (_classifiers.Length != MuseCocoBundle.Positive(bundle.Metadata, "classifierCount", 128))
        {
            throw new ModelLoadException("BERT classifier count does not match the attribute schema.");
        }
        foreach (string name in new[] { "input_ids", "attention_mask", "token_type_ids", "position_ids" })
        {
            MuseCocoBundle.RequireTensor(graph.Metadata.Inputs, name, OnnxElementType.Int64, 1, -1);
        }
        MuseCocoBundle.RequireTensor(graph.Metadata.Outputs, "logits", OnnxElementType.Float, 1,
            _classifiers.Sum(d => d.Values.Count));
        if (graph.Metadata.Inputs.Count != 4 || graph.Metadata.Outputs.Count != 1)
        {
            throw new ModelLoadException("Unexpected tensors in the BERT graph contract.");
        }
    }

    /// <summary>The shared attribute definitions and defaults.</summary>
    public MusicAttributeSchema Schema { get; }

    /// <summary>Maximum prompt length in WordPiece tokens, including CLS and SEP.</summary>
    public int MaximumSequenceLength { get; }

    /// <summary>The loaded ONNX graph's metadata.</summary>
    public OnnxModelMetadata Metadata => _graph.Metadata;

    /// <summary>The execution settings used by the loaded graph.</summary>
    public OnnxRunnerOptions Options => _graph.Options;

    /// <summary>Loads a staged text bundle from a directory. Dispose the returned model to release its weights.</summary>
    public static async Task<MuseCocoTextModel> LoadFromDirectoryAsync(
        string directory, OnnxRunnerOptions options = null, CancellationToken cancellationToken = default)
    {
        MuseCocoBundle bundle = await MuseCocoBundle.FromDirectoryAsync(directory, "text", cancellationToken).ConfigureAwait(false);
        return await LoadAsync(bundle, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Loads a staged text bundle from logical filename to physical path pairs, without copying store blobs.</summary>
    public static async Task<MuseCocoTextModel> LoadFromFilesAsync(
        IReadOnlyDictionary<string, string> files, OnnxRunnerOptions options = null, CancellationToken cancellationToken = default)
    {
        MuseCocoBundle bundle = await MuseCocoBundle.FromFilesAsync(files, "text", cancellationToken).ConfigureAwait(false);
        return await LoadAsync(bundle, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<MuseCocoTextModel> LoadAsync(MuseCocoBundle bundle, OnnxRunnerOptions options, CancellationToken cancellationToken)
    {
        IOnnxModel graph = null;
        try
        {
            var tokenizer = new BertWordPieceTokenizer(await bundle.ReadJsonAsync(
                MuseCocoBundle.String(bundle.Metadata, "tokenizer"), cancellationToken).ConfigureAwait(false));
            graph = await bundle.LoadGraphAsync(options, cancellationToken).ConfigureAwait(false);
            return new MuseCocoTextModel(graph, bundle, tokenizer);
        }
        catch (Exception error)
        {
            if (graph != null) await graph.DisposeAsync().ConfigureAwait(false);
            if (error is System.Text.Json.JsonException || error is KeyNotFoundException
                || error is InvalidOperationException || error is FormatException || error is OverflowException)
                throw new ModelLoadException("Invalid MuseCoco text bundle metadata.", error);
            throw;
        }
    }

    /// <summary>
    /// Predicts attributes from a text prompt. Long prompts are truncated to <see cref="MaximumSequenceLength"/>,
    /// reported in the result. Empty text is supported; null is rejected. Callers can edit
    /// <see cref="MusicAttributePrediction.Attributes"/> before passing it to the music model.
    /// </summary>
    public async Task<MusicAttributePrediction> PredictAsync(string text, CancellationToken cancellationToken = default)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        int previous = Interlocked.CompareExchange(ref _state, 1, 0);
        if (previous == 2) throw new ObjectDisposedException(nameof(MuseCocoTextModel));
        if (previous != 0) throw new InferenceException("A prediction is already running on this MuseCoco text model.");
        try
        {
            BertWordPieceEncoding encoded = _tokenizer.Encode(text, MaximumSequenceLength, cancellationToken);
            int length = encoded.Ids.Length;
            var positions = new long[length];
            var mask = new long[length];
            for (int i = 0; i < length; i++)
            {
                positions[i] = i;
                mask[i] = 1;
            }
            var inputs = new Dictionary<string, OnnxTensor>(StringComparer.Ordinal)
            {
                ["input_ids"] = OnnxTensor.FromInt64(encoded.Ids, 1, length),
                ["attention_mask"] = OnnxTensor.FromInt64(mask, 1, length),
                ["token_type_ids"] = OnnxTensor.FromInt64(new long[length], 1, length),
                ["position_ids"] = OnnxTensor.FromInt64(positions, 1, length)
            };
            IReadOnlyDictionary<string, OnnxTensor> output = await _graph.RunAsync(inputs, cancellationToken).ConfigureAwait(false);
            float[] logits = output["logits"].Floats;
            if (logits == null || logits.Length != _classifiers.Sum(d => d.Values.Count))
            {
                throw new InferenceException("The BERT graph returned an invalid classifier tensor.");
            }
            MusicAttributes attributes = Schema.CreateAttributes();
            var probabilities = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal);
            int offset = 0;
            foreach (MusicAttributeDefinition definition in _classifiers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int best = 0;
                for (int i = 0; i < definition.Values.Count; i++)
                {
                    if (!float.IsFinite(logits[offset + i])) throw new InferenceException("BERT returned a non-finite logit.");
                    if (logits[offset + i] > logits[offset + best]) best = i;
                }
                var values = new float[definition.Values.Count];
                double sum = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = (float)Math.Exp((double)logits[offset + i] - logits[offset + best]);
                    sum += values[i];
                }
                for (int i = 0; i < values.Length; i++) values[i] = (float)(values[i] / sum);
                probabilities.Add(definition.Name, Array.AsReadOnly(values));
                attributes = attributes.WithIndex(definition.Name, best);
                offset += values.Length;
            }
            return new MusicAttributePrediction(attributes, probabilities, encoded.Ids, encoded.Truncated);
        }
        finally
        {
            Volatile.Write(ref _state, 0);
        }
    }

    /// <summary>Releases weights. Disposing during a prediction is refused; cancel and await that prediction first.</summary>
    public void Dispose()
    {
        int previous = Interlocked.CompareExchange(ref _state, 2, 0);
        if (previous == 1) throw new InvalidOperationException("Await the active prediction before disposing the model.");
        if (previous == 0) _graph.Dispose();
    }

    /// <summary>Releases weights; equivalent to <see cref="Dispose"/> because disposal performs no I/O.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

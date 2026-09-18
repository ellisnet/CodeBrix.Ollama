using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager.Tests;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// The models the reduction tests reduce, and the steps that put each of them in the store: the publisher's own
/// graphs registered as a bundle, the MuPT checkpoint exported at single precision, and either of those prepared for
/// a quantizer. Each step is done once and then simply found, because a download is a download and an export takes
/// seconds that no second test should pay again.
/// </summary>
public static class ReduceTestModels
{
    /// <summary>The SkyTNT bundle as this library stores what its publisher shipped.</summary>
    public const string SkyTntOnnx = "hf.co/skytnt/midi-model-tv2o-medium:onnx";

    /// <summary>The same pair after ONNX Runtime's preparation pass.</summary>
    public const string SkyTntPrepared = "hf.co/skytnt/midi-model-tv2o-medium:onnx-preprocessed";

    /// <summary>The MuPT graph the builder writes, which is what the MuPT reductions start from.</summary>
    public const string MuPtFp32 = "hf.co/m-a-p/MuPT-v1-8192-190M:onnx-fp32";

    /// <summary>The same graph after ONNX Runtime's preparation pass.</summary>
    public const string MuPtPrepared = "hf.co/m-a-p/MuPT-v1-8192-190M:onnx-fp32-preprocessed";

    /// <summary>The larger of SkyTNT's two graphs.</summary>
    public const string BaseGraph = "onnx/model_base.onnx";

    /// <summary>The smaller of SkyTNT's two graphs.</summary>
    public const string TokenGraph = "onnx/model_token.onnx";

    /// <summary>The one graph the MuPT bundle holds.</summary>
    public const string MuPtGraph = "model.onnx";

    /// <summary>The shapes SkyTNT's larger graph is run with: one step, no past.</summary>
    public static readonly string[] BaseGraphDimensions =
        { "batch=1", "mid_seq=2", "token_seq=8", "past_seq=0" };

    /// <summary>The shapes SkyTNT's smaller graph is run with.</summary>
    public static readonly string[] TokenGraphDimensions =
        { "batch=1", "states=1", "token_seq=1", "past_seq=0" };

    /// <summary>
    /// The shapes the MuPT graph is run with. kv_cache_dim is the head size the builder leaves symbolic so that one
    /// export serves a plain cache and a compressed one; the attention operator checks it even when the cache is
    /// empty, so it has to be the real one.
    /// </summary>
    public static readonly string[] MuPtDimensions =
    {
        "batch_size=1", "sequence_length=4", "total_sequence_length=4", "past_sequence_length=0",
        "kv_cache_dim=64"
    };

    /// <summary>
    /// Makes sure the pass-through bundle of SkyTNT's published graphs is in the store. It costs nothing to write -
    /// the store is content addressed, so it names the blobs the pulled bundle already holds - and reducing it rather
    /// than the pulled bundle is what proves a derived bundle can be derived from again.
    /// </summary>
    /// <param name="store">The store to work in.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The name of the pass-through bundle.</returns>
    public static async Task<string> EnsurePassThroughAsync(ModelStore store, CancellationToken cancellationToken)
    {
        string pulled = await ExportTestStore.EnsureAsync(
            store, MusicModelDefinitions.SkyTntMidiModelTv2oMediumOnnxOnly, cancellationToken);

        if (!await store.ExistsAsync(SkyTntOnnx, cancellationToken))
        {
            await store.ExportToOnnxAsync(
                pulled, new ExportOptions { OutputName = SkyTntOnnx }, null, cancellationToken);
        }

        return SkyTntOnnx;
    }

    /// <summary>
    /// Makes sure the MuPT graph the builder writes at single precision is in the store, exporting it if it is not.
    /// The checkpoint itself is pulled once and kept.
    /// </summary>
    /// <param name="store">The store to work in.</param>
    /// <param name="report">Where a line about the export goes, or <see langword="null"/> to say nothing.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The name of the exported graph.</returns>
    public static async Task<string> EnsureMuPtGraphAsync(
        ModelStore store, Action<string> report, CancellationToken cancellationToken)
    {
        string checkpoint = await ExportTestStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);

        if (!await store.ExistsAsync(MuPtFp32, cancellationToken))
        {
            var options = new ExportOptions
            {
                Route = ExportRoute.GenAiBuilder,
                Precision = "fp32",
                OutputName = MuPtFp32,
                //The checkpoint defines its own tokenizer class, and the builder will not read it otherwise;
                //the export tests say the same thing about the same model.
                AllowRemoteCode = true,
                Overwrite = true
            };

            Stopwatch stopwatch = Stopwatch.StartNew();
            ExportResult exported = await store.ExportToOnnxAsync(
                checkpoint, options, null, cancellationToken);
            stopwatch.Stop();
            report?.Invoke(string.Format(
                CultureInfo.InvariantCulture,
                "exported {0} with {1} in {2:F1} s",
                exported.Name,
                exported.Tool,
                stopwatch.Elapsed.TotalSeconds));
        }

        return MuPtFp32;
    }

    /// <summary>
    /// Makes sure a prepared form of one of those bundles is in the store, running the preparation pass if it is not.
    /// </summary>
    /// <param name="store">The store to work in.</param>
    /// <param name="sourceName">The bundle to prepare.</param>
    /// <param name="preparedName">The name the prepared bundle takes.</param>
    /// <param name="report">Where a line about the preparation goes, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The name of the prepared bundle.</returns>
    /// <remarks>
    /// Preparation is the Python engine's alone, and what it writes is exactly what a quantizer is fed: shape
    /// inference has run and the graph records it. That record is what lets the managed engine quantize the result
    /// dynamically, so this is the step that makes the two engines comparable at all in that mode.
    /// </remarks>
    public static async Task<string> EnsurePreparedAsync(
        ModelStore store,
        string sourceName,
        string preparedName,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        if (!await store.ExistsAsync(preparedName, cancellationToken))
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            ReduceResult prepared = await store.ReduceOnnxAsync(
                sourceName,
                new ReduceOptions { Mode = ReduceMode.PreprocessOnly, OutputName = preparedName },
                null,
                cancellationToken);
            stopwatch.Stop();
            report?.Invoke(string.Format(
                CultureInfo.InvariantCulture,
                "prepared {0}: {1} -> {2} bytes in {3:F1} s",
                prepared.Name,
                prepared.SourceBytes,
                prepared.ReducedBytes,
                stopwatch.Elapsed.TotalSeconds));
        }

        return preparedName;
    }
}

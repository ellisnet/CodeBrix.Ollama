using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using CodeBrix.Ollama.ModelManager.Tests;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The managed ONNX interpreter against onnxruntime on the store's OWN reductions of the published MIDI
/// decoder: four-bit weights, eight-bit weights, and the dynamic eight-bit rewrite.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE CONTRACT BETWEEN THE TWO LIBRARIES, EXERCISED. The store reduces a bundle it holds, the test
/// asks it where the reduced files are, and the runner is handed those paths and nothing else: no type of
/// either library appears in the other, and this project is the only one that names both. What is being
/// proved is the plan's rule that whatever the store writes, the engine runs.
/// </para>
/// <para>
/// THE REDUCED BUNDLES ARE KEPT in the test-model cache between runs, because each is arithmetic over the
/// same weights with the same settings and gives the same answer every time; the first run makes them, and
/// every run after it finds them. They are a few hundred megabytes in a cache that already holds the
/// gigabyte they came from.
/// </para>
/// <para>
/// THE TWO WEIGHT-ONLY REDUCTIONS ARE HELD TO A HUNDREDTH and meet it with four orders to spare: the packed
/// bytes and the scales are the same on both sides, so the arithmetic is the same arithmetic. THE DYNAMIC ONE
/// IS DIFFERENT, because it quantizes the ACTIVATIONS as the graph runs, and that makes a step chaotic rather
/// than merely approximate - see <see cref="OnnxSubjectKind.QuantizedActivations"/>, which records what was
/// measured and why the bar for it is wider. The greedy choice is required to be identical for all three.
/// </para>
/// </remarks>
public sealed class ReducedSkyTntOnnxLiveTests
{
    /// <summary>The graph that turns an event's hidden state into its tokens.</summary>
    private const string TokenGraph = "onnx/model_token.onnx";

    /// <summary>The graph that turns a window of events into hidden states.</summary>
    private const string BaseGraph = "onnx/model_base.onnx";

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class.</summary>
    /// <param name="output">Where the measured numbers are written.</param>
    public ReducedSkyTntOnnxLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Both graphs reduced to four-bit weights agree with onnxruntime.</summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_four_bit_reduction() =>
        await CompareAsync(
            ReduceMode.WeightOnlyInt4, "onnx-oracle/skytnt-tv2o-medium:int4", "skytnt int4",
            OnnxSubjectKind.QuantizedWeights, TestContext.Current.CancellationToken);

    /// <summary>Both graphs reduced to eight-bit weights agree with onnxruntime.</summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_eight_bit_reduction() =>
        await CompareAsync(
            ReduceMode.WeightOnlyInt8, "onnx-oracle/skytnt-tv2o-medium:int8", "skytnt int8",
            OnnxSubjectKind.QuantizedWeights, TestContext.Current.CancellationToken);

    /// <summary>Both graphs rewritten to the dynamic eight-bit path agree with onnxruntime.</summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_dynamic_eight_bit_reduction() =>
        await CompareAsync(
            ReduceMode.DynamicInt8, "onnx-oracle/skytnt-tv2o-medium:dynamic-int8", "skytnt dynamic int8",
            OnnxSubjectKind.QuantizedActivations, TestContext.Current.CancellationToken);

    /// <summary>
    /// The four-bit model is a fraction of the full-precision one IN MEMORY, not only on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE FENCE FOR THE WHOLE QUANTIZED PATH. A loader that turned a four-bit weight back into floats
    /// would give every one of the numbers above and undo the only reason the graph was reduced. What is
    /// measured is the MANAGED HEAP either side of the load, with a full collection at both ends, because that
    /// is what the loaded model itself is holding; the process's resident memory is the wrong instrument here,
    /// since it also carries whatever the allocator has not handed back from the test before this one.
    /// </para>
    /// <para>
    /// The bar is a quarter. A four-bit weight with a scale for every hundred and twenty-eight values is about
    /// an eighth of the same weight in floats, and the graph also holds a few tensors that were never
    /// quantized, so a quarter is the round number that a packed load passes easily and an unpacked one cannot
    /// come near.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes when both models have been loaded and measured.</returns>
    [EnvGatedFact(TestGates.RunLiveTests)]
    public async Task Load_of_the_four_bit_reduction_holds_a_fraction_of_the_full_precision_weights()
    {
        //Arrange
        using ModelStore store = EndToEndStore.Open();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string source = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.SkyTntMidiModelTv2oMediumOnnxOnly, cancellationToken);
        string reduced = await EndToEndStore.EnsureReducedAsync(
            store, source, ReduceMode.WeightOnlyInt4, "onnx-oracle/skytnt-tv2o-medium:int4",
            cancellationToken);

        //Act
        long fullPrecision = await HeldAsync(store, source, cancellationToken);
        long quantized = await HeldAsync(store, reduced, cancellationToken);

        //Assert
        _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0} held by the loaded {1}, {2} by the four-bit reduction of it - a factor of {3:F1}",
            ProcessMemory.Mebibytes(fullPrecision), BaseGraph, ProcessMemory.Mebibytes(quantized),
            quantized == 0 ? 0 : (double)fullPrecision / quantized));

        quantized.Should().BeLessThan(
            fullPrecision / 4,
            "a four-bit weight must stay packed in memory, not be turned back into floats when it loads");
    }

    private async Task CompareAsync(
        ReduceMode mode, string name, string subject, OnnxSubjectKind kind,
        CancellationToken cancellationToken)
    {
        //Arrange
        string virtualEnvironment = TestGates.RequireVirtualEnvironment();
        using ModelStore store = EndToEndStore.Open();
        string source = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.SkyTntMidiModelTv2oMediumOnnxOnly, cancellationToken);
        string reduced = await EndToEndStore.EnsureReducedAsync(
            store, source, mode, name, cancellationToken);

        // The consumer's own two lines: the store says which blob holds which of the bundle's files, and the
        // runner is handed those pairs. Nothing else connects the two libraries.
        ResolvedModel resolved = await store.ResolveAsync(reduced, cancellationToken);
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ResolvedFile file in resolved.Files) files[file.Name] = file.BlobPath;

        //Act and assert
        using TempWorkDirectory work = new TempWorkDirectory();
        foreach ((string graph, string shapes) in new[] { (TokenGraph, "token"), (BaseGraph, "base") })
        {
            OnnxSubjectResult measured = OnnxSubjectComparison.Run(
                _output, virtualEnvironment, subject, files, graph, shapes, kind,
                System.IO.Path.Combine(work.DirectoryPath, shapes), cancellationToken);
            _output.WriteLine(measured.ToString());
        }
    }

    private static async Task<long> HeldAsync(
        ModelStore store, string name, CancellationToken cancellationToken)
    {
        ResolvedModel resolved = await store.ResolveAsync(name, cancellationToken);
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ResolvedFile file in resolved.Files) files[file.Name] = file.BlobPath;

        long before = GC.GetTotalMemory(forceFullCollection: true);
        await using (IOnnxModel model = await OnnxModel.LoadFromFilesAsync(
            files, BaseGraph, new OnnxRunnerOptions { Threads = 1 }, cancellationToken))
        {
            model.Metadata.Operators.Should().NotBeEmpty();
            return GC.GetTotalMemory(forceFullCollection: true) - before;
        }
    }
}

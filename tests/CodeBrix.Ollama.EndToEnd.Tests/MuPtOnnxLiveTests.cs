using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using CodeBrix.Ollama.ModelManager.Tests;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The managed ONNX interpreter against onnxruntime on a CONTRIBUTED-operator decoder: the ABC-notation
/// language model as the ONNX Runtime GenAI builder writes it, and as the store then reduces it.
/// </summary>
/// <remarks>
/// <para>
/// THESE ARE THE GRAPHS THE CONTRIBUTED OPERATORS EXIST FOR. The builder does not decompose attention into
/// twenty standard nodes the way a plain exporter does: it writes ONE <c>GroupQueryAttention</c> per layer,
/// which carries the rotary embedding, the cache, the causal mask and both matrix products, and it writes the
/// residual-and-normalize pair as one <c>SkipSimplifiedLayerNormalization</c>. Its four-bit form replaces
/// every matrix multiply with a <c>MatMulNBits</c> whose weight is never unpacked. A decoder written this way
/// is unrunnable by an engine that implements the standard operators alone.
/// </para>
/// <para>
/// THE BUNDLE IS LAID OUT ON DISK FIRST, by hard links that cost nothing, because these exports keep every
/// weight in a SIDE FILE beside the graph and a tool reading the graph looks for that file by the name the
/// graph gives it. The store holds files under their digests, so the publisher's names have to be put back
/// before another tool can be pointed at them. The runner does not need this - it takes the (logical name to
/// path) pairs the store hands out - but it is given the same laid-out files here so that both engines are
/// reading the identical bytes.
/// </para>
/// <para>
/// The exported and reduced bundles are KEPT in the test-model cache between runs; the first run makes them,
/// which costs minutes, and every run after it finds them.
/// </para>
/// <para>
/// ONLY THE BUILDER'S FOUR-BIT EXPORT CARRIES accuracy_level - on all 61 of its MatMulNBits nodes, with the
/// value 4. The store's own reductions carry NONE of it: ReduceOnnxAsync leaves the attribute off unless
/// ReduceOptions.AccuracyLevel is set, and nothing here sets it. So the stripped comparison below exists for
/// that one subject, and the reductions need no equivalent.
/// </para>
/// </remarks>
public sealed class MuPtOnnxLiveTests
{
    /// <summary>The graph the model builder writes.</summary>
    private const string Graph = "model.onnx";

    /// <summary>
    /// The name the oracle's accuracy_level-stripped copy is written under, beside the graph it came from.
    /// </summary>
    /// <remarks>
    /// It goes BESIDE the original because these exports keep their weights in a side file that the graph
    /// names relatively, and onnxruntime refuses to follow such a name out of the directory the model is in.
    /// Both files are in this test's own temporary folder, which the store materialized into by hard links.
    /// </remarks>
    private const string StrippedGraph = "model.accuracy-level-stripped.onnx";

    /// <summary>The step shapes a causal language model runs: a short prompt, then one token.</summary>
    private const string Shapes = "causal";

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class.</summary>
    /// <param name="output">Where the measured numbers are written.</param>
    public MuPtOnnxLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>The builder's full-precision export agrees with onnxruntime.</summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_builder_export() =>
        await CompareExportAsync(
            "fp32", "onnx-oracle/mupt-190m:fp32", "mupt builder fp32",
            OnnxSubjectKind.FullPrecision, TestContext.Current.CancellationToken);

    /// <summary>The builder's four-bit export agrees with onnxruntime.</summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_builder_four_bit_export() =>
        await CompareExportAsync(
            "int4", "onnx-oracle/mupt-190m:int4", "mupt builder int4",
            OnnxSubjectKind.LowerPrecisionByRequest, TestContext.Current.CancellationToken);

    /// <summary>
    /// The builder's four-bit export agrees with onnxruntime to the ORDINARY quantized-weight tolerance once
    /// onnxruntime is asked for the same precision this engine already computes at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE CORRECTNESS PROOF FOR THAT SUBJECT, and it is the reason the unstripped comparison above
    /// is allowed its wide bar. That export carries accuracy_level=4 on all 61 of its MatMulNBits nodes,
    /// which asks a runtime to quantize the ACTIVATIONS to 8-bit integers as well; onnxruntime takes the
    /// option and this engine does not, so the two differ by a few percent and two of four greedy choices can
    /// differ with them. That is a difference in PRECISION, not in the operator - and the way to show it is
    /// to take the attribute away and see the difference vanish.
    /// </para>
    /// <para>
    /// THE TWO ENGINES ARE GIVEN DIFFERENT FILES HERE, on purpose and for the only time in this suite. The
    /// oracle runs a copy of the graph with the attribute stripped out, written into this test's own
    /// temporary folder beside the model it came from - the stored bundle is never touched, and the side
    /// file holding the weights is neither rewritten nor copied. THE MANAGED ENGINE RUNS THE ORIGINAL,
    /// UNMODIFIED GRAPH: it reads accuracy_level and ignores it, so its answer is the same either way, and
    /// giving it the original is what makes that claim part of what the test proves rather than something
    /// taken on trust.
    /// </para>
    /// <para>
    /// What it asserts is therefore the full quantized-weight bar - a hundredth, where the measured figure is
    /// about three millionths - AND an identical greedy choice, both steps, at one thread and at eight.
    /// </para>
    /// </remarks>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_builder_four_bit_export_without_its_accuracy_level()
    {
        //Arrange
        using ModelStore store = EndToEndStore.Open();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string source = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        string exported = await EndToEndStore.EnsureExportedAsync(
            store, source, "int4", "onnx-oracle/mupt-190m:int4", cancellationToken);

        //Act and assert
        await CompareAsync(
            store, exported, "mupt builder int4, accuracy_level stripped",
            OnnxSubjectKind.QuantizedWeights, cancellationToken, StrippedGraph);
    }

    /// <summary>The store's four-bit reduction of the builder's export agrees with onnxruntime.</summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_four_bit_reduction() =>
        await CompareReductionAsync(
            ReduceMode.WeightOnlyInt4, "onnx-oracle/mupt-190m:fp32-int4", "mupt reduced int4",
            OnnxSubjectKind.QuantizedWeights, TestContext.Current.CancellationToken);

    /// <summary>The store's eight-bit reduction of the builder's export agrees with onnxruntime.</summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_eight_bit_reduction() =>
        await CompareReductionAsync(
            ReduceMode.WeightOnlyInt8, "onnx-oracle/mupt-190m:fp32-int8", "mupt reduced int8",
            OnnxSubjectKind.QuantizedWeights, TestContext.Current.CancellationToken);

    /// <summary>The store's dynamic eight-bit reduction of the builder's export agrees with onnxruntime.</summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_dynamic_eight_bit_reduction() =>
        await CompareReductionAsync(
            ReduceMode.DynamicInt8, "onnx-oracle/mupt-190m:fp32-dynamic-int8", "mupt reduced dynamic int8",
            OnnxSubjectKind.QuantizedActivations, TestContext.Current.CancellationToken);

    private async Task CompareExportAsync(
        string precision, string name, string subject, OnnxSubjectKind kind,
        CancellationToken cancellationToken)
    {
        using ModelStore store = EndToEndStore.Open();
        string source = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        string exported = await EndToEndStore.EnsureExportedAsync(
            store, source, precision, name, cancellationToken);
        await CompareAsync(store, exported, subject, kind, cancellationToken);
    }

    private async Task CompareReductionAsync(
        ReduceMode mode, string name, string subject, OnnxSubjectKind kind,
        CancellationToken cancellationToken)
    {
        using ModelStore store = EndToEndStore.Open();
        string source = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        string exported = await EndToEndStore.EnsureExportedAsync(
            store, source, "fp32", "onnx-oracle/mupt-190m:fp32", cancellationToken);
        string reduced = await EndToEndStore.EnsureReducedAsync(
            store, exported, mode, name, cancellationToken);
        await CompareAsync(store, reduced, subject, kind, cancellationToken);
    }

    private async Task CompareAsync(
        ModelStore store, string name, string subject, OnnxSubjectKind kind,
        CancellationToken cancellationToken, string strippedGraph = null)
    {
        //Arrange
        string virtualEnvironment = TestGates.RequireVirtualEnvironment();
        using TempWorkDirectory work = new TempWorkDirectory("bundle");
        IReadOnlyList<string> written = await store.MaterializeAsync(
            name, work.DirectoryPath, null, cancellationToken);
        written.Should().NotBeEmpty();

        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in written)
        {
            files[Path.GetRelativePath(work.DirectoryPath, path).Replace('\\', '/')] = path;
        }

        files.Should().ContainKey(Graph);

        //Act and assert
        OnnxSubjectResult measured = OnnxSubjectComparison.Run(
            _output, virtualEnvironment, subject, files, Graph, Shapes, kind,
            work.Combine("oracle"), cancellationToken,
            strippedGraph == null ? null : Path.Combine(work.DirectoryPath, strippedGraph));
        _output.WriteLine(measured.ToString());
    }
}

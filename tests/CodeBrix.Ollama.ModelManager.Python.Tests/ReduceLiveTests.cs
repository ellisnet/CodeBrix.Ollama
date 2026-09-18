using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager.Tests;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// The tests that really reduce real models: SkyTNT's published ONNX pair in all four modes, and the
/// MuPT 190M graph the GenAI builder writes at single precision in the two that make it smallest. Each
/// one checks the derived bundle the way a consumer would - it lists, it shows its provenance, it
/// resolves and it materializes - measures how much smaller the graphs became, RUNS the reduced model
/// against the one it came from, and then removes what it wrote.
/// </summary>
/// <remarks>
/// <para>
/// TWO GATES, as for the export tests: CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 for the download and
/// CODEBRIX_OLLAMA_RUN_PYTHON_TESTS=1 for the interpreter. The source models are pulled into the
/// test-model cache and KEPT there; everything these tests derive is removed when each test ends.
/// </para>
/// <para>
/// A reduction reads and writes gigabytes through the system temporary directory. On a machine whose
/// temporary directory is in memory - which is most Linux desktops - point TMPDIR at a real file system
/// before opening these gates.
/// </para>
/// <para>
/// THE MEASURED SIZES AND DIFFERENCES ARE THE POINT and are written to the run's output with
/// -showLiveOutput, because a ratio nobody wrote down is a ratio nobody can check next time.
/// </para>
/// </remarks>
public sealed class ReduceLiveTests
{
    /// <summary>The SkyTNT bundle as this library stores what its publisher shipped.</summary>
    private const string SkyTntOnnx = ReduceTestModels.SkyTntOnnx;

    /// <summary>The MuPT graph the builder writes, which is what the MuPT reductions start from.</summary>
    private const string MuPtFp32 = ReduceTestModels.MuPtFp32;

    /// <summary>The larger of SkyTNT's two graphs.</summary>
    private const string BaseGraph = ReduceTestModels.BaseGraph;

    /// <summary>The smaller of SkyTNT's two graphs.</summary>
    private const string TokenGraph = ReduceTestModels.TokenGraph;

    /// <summary>How long a validation run is given before its child process is killed.</summary>
    private static readonly TimeSpan ValidationWait = TimeSpan.FromMinutes(20);

    /// <summary>The shapes SkyTNT's larger graph is run with: one step, no past.</summary>
    private static readonly string[] BaseGraphDimensions = ReduceTestModels.BaseGraphDimensions;

    /// <summary>The shapes SkyTNT's smaller graph is run with.</summary>
    private static readonly string[] TokenGraphDimensions = ReduceTestModels.TokenGraphDimensions;

    /// <summary>The shapes the MuPT graph is run with.</summary>
    private static readonly string[] MuPtDimensions = ReduceTestModels.MuPtDimensions;

    /// <summary>The assembly's one interpreter, taken so that the gate and the environment agree.</summary>
    private readonly PythonTestFixture _fixture;

    /// <summary>Where the sizes, ratios and differences are written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Takes the assembly's interpreter and the output the measurements go to.
    /// </summary>
    /// <param name="fixture">The assembly fixture.</param>
    /// <param name="output">Where each reduction reports its sizes and differences.</param>
    public ReduceLiveTests(PythonTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public Task the_skytnt_pair_is_reduced_to_dynamic_eight_bit_weights()
        => ReduceSkyTntAsync(ReduceMode.DynamicInt8, SkyTntOnnx + "-int8", 2.5, validateBoth: true, 0.30);

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public Task the_skytnt_pair_is_reduced_to_block_wise_eight_bit_weights()
        => ReduceSkyTntAsync(ReduceMode.WeightOnlyInt8, SkyTntOnnx + "-int8-weights", 2.5, false, 0.30);

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public Task the_skytnt_pair_is_reduced_to_block_wise_four_bit_weights()
        => ReduceSkyTntAsync(ReduceMode.WeightOnlyInt4, SkyTntOnnx + "-int4-weights", 4.0, true, 0.80);

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_skytnt_pair_is_prepared_for_a_quantizer_without_being_quantized()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string sourceName = await EnsurePassThroughAsync(store, cancellationToken);
        const string outputName = SkyTntOnnx + "-preprocessed";
        await ExportTestStore.RemoveAsync(store, outputName, cancellationToken);

        //Act
        Stopwatch stopwatch = Stopwatch.StartNew();
        ReduceResult result = await store.ReduceOnnxAsync(
            sourceName, new ReduceOptions { Mode = ReduceMode.PreprocessOnly }, null, cancellationToken);
        stopwatch.Stop();

        try
        {
            //Assert
            result.Name.Should().Be(outputName);
            result.Mode.Should().Be(ReduceMode.PreprocessOnly);
            result.Files.Should().Contain(BaseGraph);
            result.Files.Should().Contain(TokenGraph);
            ReportSizes("SkyTNT PreprocessOnly", result, stopwatch.Elapsed);

            ModelInfo info = await store.ShowAsync(outputName, cancellationToken);
            info.Settings["mode"].Should().Be("PreprocessOnly");
            info.DerivedFrom.Should().Be(sourceName);

            //A prepared graph must still compute what it computed before: nothing has been quantized.
            using var prepared = new TempExportDirectory();
            using var original = new TempExportDirectory();
            await store.MaterializeAsync(outputName, prepared.DirectoryPath, null, cancellationToken);
            await store.MaterializeAsync(sourceName, original.DirectoryPath, null, cancellationToken);

            VenvPythonRun validation = Validate(
                "SkyTNT model_token prepared",
                Path.Combine(original.DirectoryPath, TokenGraph),
                Path.Combine(prepared.DirectoryPath, TokenGraph),
                TokenGraphDimensions,
                inventory: false,
                cancellationToken);
            validation.Number("max-abs-diff").Should().BeLessThan(1e-3);
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, outputName, cancellationToken);
            await ExportTestStore.RemoveAsync(store, sourceName, cancellationToken);
        }
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_bundle_as_it_was_pulled_is_reduced_without_being_exported_first()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        MusicModel model = MusicModelDefinitions.SkyTntMidiModelTv2oMediumOnnxOnly;
        string sourceName = await ExportTestStore.EnsureAsync(store, model, cancellationToken);
        ResolvedModel source = await store.ResolveAsync(sourceName, cancellationToken);
        var options = new ReduceOptions
        {
            Mode = ReduceMode.WeightOnlyInt4,
            Files = new[] { TokenGraph },
            OutputName = "hf.co/skytnt/midi-model-tv2o-medium:one-graph-int4",
            Overwrite = true
        };

        //Act
        Stopwatch stopwatch = Stopwatch.StartNew();
        ReduceResult result = await store.ReduceOnnxAsync(
            sourceName, options, null, cancellationToken);
        stopwatch.Stop();

        try
        {
            //Assert
            ReportSizes("SkyTNT model_token only, int4", result, stopwatch.Elapsed);
            result.EngineUsed.Should().Be(
                ReduceEngine.Managed,
                "a weight-only reduction is this library's own work, whatever is installed");
            result.Tool.Should().Be("CodeBrix.Ollama.ModelManager");
            result.SourceBytes.Should().Be(
                source.Files.Single(file => file.Name == TokenGraph).Size);
            result.ReducedBytes.Should().BeLessThan(result.SourceBytes);

            ResolvedModel derived = await store.ResolveAsync(result.Name, cancellationToken);
            derived.Files.Single(file => file.Name == BaseGraph).Digest
                .Should().Be(source.Files.Single(file => file.Name == BaseGraph).Digest,
                    "a graph nobody asked to reduce is carried through, not rewritten");
            derived.Files.Single(file => file.Name == TokenGraph).Size
                .Should().BeLessThan(source.Files.Single(file => file.Name == TokenGraph).Size);
            derived.Files.Select(file => file.Name).Should().Contain("README.md");

            ModelInfo info = await store.ShowAsync(result.Name, cancellationToken);
            info.Settings["files"].Should().Be(TokenGraph);
            info.DerivedFrom.Should().Be(sourceName);
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, result.Name, cancellationToken);
        }
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public Task the_mupt_graph_is_reduced_to_block_wise_four_bit_weights()
        => ReduceMuPtAsync(ReduceMode.WeightOnlyInt4, MuPtFp32 + "-int4-weights", 2.5, 0.90);

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public Task the_mupt_graph_is_reduced_to_dynamic_eight_bit_weights()
        => ReduceMuPtAsync(ReduceMode.DynamicInt8, MuPtFp32 + "-int8", 2.0, 0.40);

    /// <summary>
    /// Reduces SkyTNT's published pair in one mode and checks what came out of it.
    /// </summary>
    /// <param name="mode">The mode to run.</param>
    /// <param name="expectedName">The name the derived bundle must take.</param>
    /// <param name="leastRatio">The smallest size ratio this mode may produce and still be believed.</param>
    /// <param name="validateBoth">Whether both graphs are run, or only the smaller one.</param>
    /// <param name="largestDifference">
    /// The largest relative difference between the two models' outputs that is still expected.
    /// </param>
    /// <returns>A task that completes when the bundle has been checked and removed.</returns>
    private async Task ReduceSkyTntAsync(
        ReduceMode mode, string expectedName, double leastRatio, bool validateBoth, double largestDifference)
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string sourceName = await EnsurePassThroughAsync(store, cancellationToken);
        await ExportTestStore.RemoveAsync(store, expectedName, cancellationToken);
        var progress = new RecordingProgress();

        //Act
        Stopwatch stopwatch = Stopwatch.StartNew();
        ReduceResult result = await store.ReduceOnnxAsync(
            sourceName,
            //Named outright: these are the PYTHON engine's live tests, and the sizes they measure are that
            //engine's. The two engines are compared against each other in ReduceEngineComparisonLiveTests.
            new ReduceOptions { Mode = mode, Engine = ReduceEngine.Python },
            progress,
            cancellationToken);
        stopwatch.Stop();

        try
        {
            //Assert
            result.Name.Should().Be(expectedName);
            result.Mode.Should().Be(mode);
            result.EngineUsed.Should().Be(ReduceEngine.Python);
            result.Tool.Should().Be("onnxruntime");
            result.ToolVersion.Should().NotBeNullOrEmpty();
            result.Files.Should().Equal(
                "README.md", "config.json", "generation_config.json", BaseGraph, TokenGraph);
            ReportSizes("SkyTNT " + mode, result, stopwatch.Elapsed);

            result.ReducedBytes.Should().BeLessThan(result.SourceBytes);
            Ratio(result).Should().BeGreaterThan(leastRatio);
            progress.Statuses.Should().Contain("reducing " + BaseGraph);

            ModelInfo info = await store.ShowAsync(expectedName, cancellationToken);
            info.DerivedFrom.Should().Be(sourceName);
            info.Tool.Should().Be("onnxruntime");
            info.ToolVersion.Should().Be(result.ToolVersion);
            info.Format.Should().Be("onnx");
            info.License.LicenseId.Should().Be("apache-2.0");
            info.Settings["mode"].Should().Be(mode.ToString());
            info.Settings["engine"].Should().Be("Python");
            info.Settings["files"].Should().Be(BaseGraph + "," + TokenGraph);

            (await store.ListAsync(cancellationToken)).Select(summary => summary.DisplayName)
                .Should().Contain(expectedName);

            ReportFiles(mode.ToString(), await store.ResolveAsync(expectedName, cancellationToken));

            using var reduced = new TempExportDirectory();
            using var original = new TempExportDirectory();
            IReadOnlyList<string> written = await store.MaterializeAsync(
                expectedName, reduced.DirectoryPath, null, cancellationToken);
            written.Should().HaveCount(5);
            await store.MaterializeAsync(sourceName, original.DirectoryPath, null, cancellationToken);

            Check(
                "SkyTNT model_token " + mode,
                Path.Combine(original.DirectoryPath, TokenGraph),
                Path.Combine(reduced.DirectoryPath, TokenGraph),
                TokenGraphDimensions,
                largestDifference,
                cancellationToken);

            if (validateBoth)
            {
                Check(
                    "SkyTNT model_base " + mode,
                    Path.Combine(original.DirectoryPath, BaseGraph),
                    Path.Combine(reduced.DirectoryPath, BaseGraph),
                    BaseGraphDimensions,
                    largestDifference,
                    cancellationToken);
            }
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, expectedName, cancellationToken);
            await ExportTestStore.RemoveAsync(store, sourceName, cancellationToken);
        }
    }

    /// <summary>
    /// Exports the MuPT checkpoint at single precision, reduces that graph in one mode, and checks what
    /// came out of it.
    /// </summary>
    /// <param name="mode">The mode to run.</param>
    /// <param name="expectedName">The name the derived bundle must take.</param>
    /// <param name="leastRatio">The smallest size ratio this mode may produce and still be believed.</param>
    /// <param name="largestDifference">
    /// The largest relative difference between the two models' outputs that is still expected.
    /// </param>
    /// <returns>A task that completes when the bundle has been checked and removed.</returns>
    private async Task ReduceMuPtAsync(
        ReduceMode mode, string expectedName, double leastRatio, double largestDifference)
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string sourceName = await EnsureMuPtGraphAsync(store, cancellationToken);
        await ExportTestStore.RemoveAsync(store, expectedName, cancellationToken);

        //Act
        Stopwatch stopwatch = Stopwatch.StartNew();
        ReduceResult result = await store.ReduceOnnxAsync(
            sourceName, new ReduceOptions { Mode = mode, Engine = ReduceEngine.Python }, null,
            cancellationToken);
        stopwatch.Stop();

        try
        {
            //Assert
            result.Name.Should().Be(expectedName);
            result.Files.Should().Contain("model.onnx");
            result.Files.Should().Contain("genai_config.json");
            ReportSizes("MuPT 190M " + mode, result, stopwatch.Elapsed);

            result.ReducedBytes.Should().BeLessThan(result.SourceBytes);
            Ratio(result).Should().BeGreaterThan(leastRatio);

            ModelInfo info = await store.ShowAsync(expectedName, cancellationToken);
            info.DerivedFrom.Should().Be(sourceName);
            info.Format.Should().Be("onnx");
            info.Settings["mode"].Should().Be(mode.ToString());

            ReportFiles(mode.ToString(), await store.ResolveAsync(expectedName, cancellationToken));

            using var reduced = new TempExportDirectory();
            using var original = new TempExportDirectory();
            await store.MaterializeAsync(expectedName, reduced.DirectoryPath, null, cancellationToken);
            await store.MaterializeAsync(sourceName, original.DirectoryPath, null, cancellationToken);

            Check(
                "MuPT 190M " + mode,
                Path.Combine(original.DirectoryPath, "model.onnx"),
                Path.Combine(reduced.DirectoryPath, "model.onnx"),
                MuPtDimensions,
                largestDifference,
                cancellationToken);
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, expectedName, cancellationToken);
            await ExportTestStore.RemoveAsync(store, sourceName, cancellationToken);
        }
    }

    /// <summary>
    /// Makes sure the pass-through bundle of SkyTNT's published graphs is in the store.
    /// </summary>
    /// <param name="store">The store to work in.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The name of the pass-through bundle.</returns>
    private static Task<string> EnsurePassThroughAsync(ModelStore store, CancellationToken cancellationToken)
        => ReduceTestModels.EnsurePassThroughAsync(store, cancellationToken);

    /// <summary>
    /// Makes sure the MuPT graph the builder writes at single precision is in the store.
    /// </summary>
    /// <param name="store">The store to work in.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>The name of the exported graph.</returns>
    private Task<string> EnsureMuPtGraphAsync(ModelStore store, CancellationToken cancellationToken)
        => ReduceTestModels.EnsureMuPtGraphAsync(store, _output.WriteLine, cancellationToken);

    /// <summary>
    /// Runs the full-precision graph and the reduced one over the same inputs, writes every measurement
    /// to the run's output, and asserts the little that must be true of any reduction: it ran, it
    /// produced finite numbers of the same shape, and it did not wander further from the original than
    /// this mode was measured to wander.
    /// </summary>
    /// <param name="label">What is being compared.</param>
    /// <param name="fp32Path">The graph as it was.</param>
    /// <param name="reducedPath">The graph as it is now.</param>
    /// <param name="dimensions">The shapes to run at.</param>
    /// <param name="largestDifference">The largest relative difference still expected.</param>
    /// <param name="cancellationToken">A token that cancels the wait for the child process.</param>
    private void Check(
        string label,
        string fp32Path,
        string reducedPath,
        IReadOnlyList<string> dimensions,
        double largestDifference,
        CancellationToken cancellationToken)
    {
        VenvPythonRun run = Validate(label, fp32Path, reducedPath, dimensions, true, cancellationToken);

        run.Value("finite").Should().Be("True", run.Report());
        run.Value("shapes-match").Should().Be("True", run.Report());
        run.Number("max-rel-diff").Should().BeLessThan(largestDifference, run.Report());
    }

    /// <summary>
    /// Runs the validation script and writes everything it printed to the run's output.
    /// </summary>
    /// <param name="label">What is being compared.</param>
    /// <param name="fp32Path">The graph as it was.</param>
    /// <param name="reducedPath">The graph as it is now.</param>
    /// <param name="dimensions">The shapes to run at.</param>
    /// <param name="inventory">Whether to report what the original graph is made of.</param>
    /// <param name="cancellationToken">A token that cancels the wait for the child process.</param>
    /// <returns>What the script printed.</returns>
    private VenvPythonRun Validate(
        string label,
        string fp32Path,
        string reducedPath,
        IReadOnlyList<string> dimensions,
        bool inventory,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "--fp32", fp32Path,
            "--reduced", reducedPath,
            "--label", label
        };
        if (inventory)
        {
            arguments.Add("--inventory");
        }
        arguments.Add("--dims");
        arguments.AddRange(dimensions);

        VenvPythonRun run = VenvPython.Run(
            _fixture.VirtualEnvironment, "validate_outputs.py", arguments, ValidationWait, cancellationToken);

        _output.WriteLine("--- validation: " + label);
        foreach (string line in (run.Output ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Trim().Length > 0)
            {
                _output.WriteLine("    " + line.Trim());
            }
        }

        run.Exited.Should().BeTrue("the validation script must end by itself." + run.Report());
        run.ExitCode.Should().Be(0, run.Report());
        run.Value("onnxruntime").Should().NotBeNullOrEmpty(run.Report());

        return run;
    }

    /// <summary>
    /// Writes the size of every graph a derived bundle holds where the run's output keeps it, which is
    /// what makes a size table per FILE rather than per bundle.
    /// </summary>
    /// <param name="mode">The mode that produced them.</param>
    /// <param name="derived">The derived bundle, resolved.</param>
    private void ReportFiles(string mode, ResolvedModel derived)
    {
        foreach (ResolvedFile file in derived.Files)
        {
            if (file.Name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
                || file.Name.EndsWith(".data", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture, "file: {0} {1} {2}", mode, file.Name, file.Size));
            }
        }
    }

    /// <summary>
    /// How many times smaller the graphs became.
    /// </summary>
    /// <param name="result">What the reduction reported.</param>
    /// <returns>The ratio of what was to what is.</returns>
    private static double Ratio(ReduceResult result)
        => result.ReducedBytes > 0 ? result.SourceBytes / (double)result.ReducedBytes : 0.0;

    /// <summary>
    /// Writes one reduction's sizes, ratio and duration where the run's output keeps them.
    /// </summary>
    /// <param name="what">What was reduced.</param>
    /// <param name="result">What the reduction reported.</param>
    /// <param name="elapsed">How long it took.</param>
    private void ReportSizes(string what, ReduceResult result, TimeSpan elapsed)
        => _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0}: {1} -> {2} bytes ({3:F1} MiB -> {4:F1} MiB, {5:F2}x) in {6:F1} s as {7}",
            what,
            result.SourceBytes,
            result.ReducedBytes,
            result.SourceBytes / (1024.0 * 1024.0),
            result.ReducedBytes / (1024.0 * 1024.0),
            Ratio(result),
            elapsed.TotalSeconds,
            result.Name));
}

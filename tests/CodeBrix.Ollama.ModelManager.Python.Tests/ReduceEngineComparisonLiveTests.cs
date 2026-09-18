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
/// THE COMPARISON THE MANAGED ENGINE EXISTS TO PASS: both engines reduce the same real models, in every mode the
/// managed one covers, and what they write has to be the same file - the same graph, node for node, and every
/// initializer byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// A synthetic graph of one MatMul says nothing about a transformer. These tests run SkyTNT's two published graphs
/// (109 and 29 matrix multiplies, embeddings read by Gather, sixty Transposes) and the MuPT graph the ONNX Runtime
/// GenAI builder writes with its weights in a file beside it, because every one of those shapes is a way for a port
/// to be wrong that a fixture cannot show.
/// </para>
/// <para>
/// The DYNAMIC comparison runs on the PREPARED bundle, and that is not a convenience: dynamic quantization reads the
/// shapes a preparation pass infers, the managed engine infers none, and preparation is the Python engine's work. So
/// the prepared bundle is the shared input, and what is compared is the quantization itself.
/// </para>
/// <para>
/// TWO GATES, as for every live test here: CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 for the downloads and
/// CODEBRIX_OLLAMA_RUN_PYTHON_TESTS=1 for the interpreter. The sources are pulled into the test-model cache and kept;
/// everything these tests derive is removed when each test ends. Point TMPDIR at a real file system first.
/// </para>
/// </remarks>
public sealed class ReduceEngineComparisonLiveTests
{
    /// <summary>How long a validation run is given before its child process is killed.</summary>
    private static readonly TimeSpan ValidationWait = TimeSpan.FromMinutes(20);

    /// <summary>The measurements the two engines have to agree on to the last digit.</summary>
    private static readonly string[] ValidationMeasurements =
        { "max-abs-diff", "max-rel-diff", "top1-agreement", "fp32-outputs", "reduced-outputs" };

    /// <summary>The assembly's one interpreter, taken so that the gate and the environment agree.</summary>
    private readonly PythonTestFixture _fixture;

    /// <summary>Where the sizes, timings and differences are written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Takes the assembly's interpreter and the output the measurements go to.
    /// </summary>
    /// <param name="fixture">The assembly fixture.</param>
    /// <param name="output">Where each comparison reports what it measured.</param>
    public ReduceEngineComparisonLiveTests(PythonTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_two_engines_write_the_same_four_bit_weights_for_the_skytnt_pair()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string source = await ReduceTestModels.EnsurePassThroughAsync(store, cancellationToken);

        //Act and assert
        try
        {
            await CompareAsync(
                store, "SkyTNT pair", source, ReduceMode.WeightOnlyInt4,
                ReduceTestModels.TokenGraph, ReduceTestModels.TokenGraphDimensions, cancellationToken);
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, source, cancellationToken);
        }
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_two_engines_write_the_same_eight_bit_weights_for_the_skytnt_pair()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string source = await ReduceTestModels.EnsurePassThroughAsync(store, cancellationToken);

        //Act and assert
        try
        {
            await CompareAsync(
                store, "SkyTNT pair", source, ReduceMode.WeightOnlyInt8,
                ReduceTestModels.TokenGraph, ReduceTestModels.TokenGraphDimensions, cancellationToken);
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, source, cancellationToken);
        }
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_two_engines_quantize_the_prepared_skytnt_pair_the_same_way()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string passThrough = await ReduceTestModels.EnsurePassThroughAsync(store, cancellationToken);
        string source = await ReduceTestModels.EnsurePreparedAsync(
            store, passThrough, ReduceTestModels.SkyTntPrepared, _output.WriteLine, cancellationToken);

        //Act and assert
        try
        {
            await CompareAsync(
                store, "SkyTNT pair prepared", source, ReduceMode.DynamicInt8,
                ReduceTestModels.TokenGraph, ReduceTestModels.TokenGraphDimensions, cancellationToken);
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, ReduceTestModels.SkyTntPrepared, cancellationToken);
            await ExportTestStore.RemoveAsync(store, passThrough, cancellationToken);
        }
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_two_engines_write_the_same_four_bit_weights_for_the_mupt_graph()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string source = await ReduceTestModels.EnsureMuPtGraphAsync(
            store, _output.WriteLine, cancellationToken);

        //Act and assert
        try
        {
            await CompareAsync(
                store, "MuPT 190M", source, ReduceMode.WeightOnlyInt4,
                ReduceTestModels.MuPtGraph, ReduceTestModels.MuPtDimensions, cancellationToken);
        }
        finally
        {
            //The export is hundreds of megabytes of this test's own making; the checkpoint it came from
            //stays in the cache, and the next test that wants the graph exports it again in a second.
            await ExportTestStore.RemoveAsync(store, source, cancellationToken);
        }
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_two_engines_write_the_same_eight_bit_weights_for_the_mupt_graph()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string source = await ReduceTestModels.EnsureMuPtGraphAsync(
            store, _output.WriteLine, cancellationToken);

        //Act and assert
        try
        {
            await CompareAsync(
                store, "MuPT 190M", source, ReduceMode.WeightOnlyInt8,
                ReduceTestModels.MuPtGraph, ReduceTestModels.MuPtDimensions, cancellationToken);
        }
        finally
        {
            //The export is hundreds of megabytes of this test's own making; the checkpoint it came from
            //stays in the cache, and the next test that wants the graph exports it again in a second.
            await ExportTestStore.RemoveAsync(store, source, cancellationToken);
        }
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_two_engines_quantize_the_prepared_mupt_graph_the_same_way()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = ExportTestStore.Open();
        string exported = await ReduceTestModels.EnsureMuPtGraphAsync(
            store, _output.WriteLine, cancellationToken);
        string source = await ReduceTestModels.EnsurePreparedAsync(
            store, exported, ReduceTestModels.MuPtPrepared, _output.WriteLine, cancellationToken);

        //Act and assert
        try
        {
            await CompareAsync(
                store, "MuPT 190M prepared", source, ReduceMode.DynamicInt8,
                ReduceTestModels.MuPtGraph, ReduceTestModels.MuPtDimensions, cancellationToken);
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, ReduceTestModels.MuPtPrepared, cancellationToken);
            await ExportTestStore.RemoveAsync(store, exported, cancellationToken);
        }
    }

    /// <summary>
    /// Reduces one bundle with each engine, compares everything they wrote, runs both results and compares what they
    /// compute, and removes both derived bundles afterwards.
    /// </summary>
    /// <param name="store">The store to work in.</param>
    /// <param name="label">What is being compared, for the run's output.</param>
    /// <param name="sourceName">The bundle to reduce.</param>
    /// <param name="mode">The mode to run.</param>
    /// <param name="validationGraph">The graph both results are run over, as the bundle spells its path.</param>
    /// <param name="dimensions">The shapes to run it at.</param>
    /// <param name="cancellationToken">A token that cancels the work.</param>
    /// <returns>A task that completes when both bundles have been compared and removed.</returns>
    private async Task CompareAsync(
        ModelStore store,
        string label,
        string sourceName,
        ReduceMode mode,
        string validationGraph,
        IReadOnlyList<string> dimensions,
        CancellationToken cancellationToken)
    {
        //Arrange
        string pythonName = sourceName + "-cmp-python";
        string managedName = sourceName + "-cmp-managed";
        await ExportTestStore.RemoveAsync(store, pythonName, cancellationToken);
        await ExportTestStore.RemoveAsync(store, managedName, cancellationToken);

        //Act
        Stopwatch pythonClock = Stopwatch.StartNew();
        ReduceResult python = await store.ReduceOnnxAsync(
            sourceName,
            new ReduceOptions
            {
                Mode = mode,
                Engine = ReduceEngine.Python,
                //The prepared bundle IS the preparation; running it again would compare two different inputs.
                Preprocess = false,
                OutputName = pythonName,
                Overwrite = true
            },
            null,
            cancellationToken);
        pythonClock.Stop();

        Stopwatch managedClock = Stopwatch.StartNew();
        ReduceResult managed = await store.ReduceOnnxAsync(
            sourceName,
            //Left to the library on purpose: the rule that picks the managed engine is part of what is tested.
            new ReduceOptions { Mode = mode, OutputName = managedName, Overwrite = true },
            null,
            cancellationToken);
        managedClock.Stop();

        try
        {
            //Assert
            python.EngineUsed.Should().Be(ReduceEngine.Python);
            managed.EngineUsed.Should().Be(
                ReduceEngine.Managed,
                "the automatic choice must reach the managed engine for a mode it covers, on a real model");
            managed.Tool.Should().Be("CodeBrix.Ollama.ModelManager");
            managed.ToolVersion.Should().Be(typeof(ModelStore).Assembly.GetName().Version.ToString());
            managed.SourceBytes.Should().Be(python.SourceBytes);

            _output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}: python {2} bytes in {3:F1} s, managed {4} bytes in {5:F1} s",
                label,
                mode,
                python.ReducedBytes,
                pythonClock.Elapsed.TotalSeconds,
                managed.ReducedBytes,
                managedClock.Elapsed.TotalSeconds));

            using var pythonFolder = new TempExportDirectory();
            using var managedFolder = new TempExportDirectory();
            using var sourceFolder = new TempExportDirectory();
            await store.MaterializeAsync(pythonName, pythonFolder.DirectoryPath, null, cancellationToken);
            await store.MaterializeAsync(managedName, managedFolder.DirectoryPath, null, cancellationToken);
            await store.MaterializeAsync(sourceName, sourceFolder.DirectoryPath, null, cancellationToken);

            var failures = new List<string>();
            foreach (string graph in python.Files.Where(IsGraph))
            {
                string left = Path.Combine(
                    pythonFolder.DirectoryPath, graph.Replace('/', Path.DirectorySeparatorChar));
                string right = Path.Combine(
                    managedFolder.DirectoryPath, graph.Replace('/', Path.DirectorySeparatorChar));

                managed.Files.Should().Contain(graph);
                ReportGraph(label, mode, graph, left, right);

                IReadOnlyList<string> differences = await DifferencesAsync(left, right, cancellationToken);
                foreach (string difference in differences)
                {
                    failures.Add(graph + ": " + difference);
                }

                if (differences.Count == 0 && SameLayout(left, right))
                {
                    byte[] expected = await File.ReadAllBytesAsync(left, cancellationToken);
                    byte[] actual = await File.ReadAllBytesAsync(right, cancellationToken);
                    if (!expected.AsSpan().SequenceEqual(actual))
                    {
                        failures.Add(graph + ": the encoded bytes differ although every compared field matches");
                    }
                }
            }

            failures.Should().BeEmpty();

            if (SameLayout(
                    Path.Combine(
                        pythonFolder.DirectoryPath, validationGraph.Replace('/', Path.DirectorySeparatorChar)),
                    Path.Combine(
                        managedFolder.DirectoryPath, validationGraph.Replace('/', Path.DirectorySeparatorChar))))
            {
                managed.ReducedBytes.Should().Be(
                    python.ReducedBytes, "the same graph written the same way is the same number of bytes");
            }

            //Both results are run, over the same inputs, against the graph they came from: what the managed engine
            //produced has to behave exactly as what the Python engine produced, measurement for measurement.
            string sourceGraph = Path.Combine(
                sourceFolder.DirectoryPath, validationGraph.Replace('/', Path.DirectorySeparatorChar));
            VenvPythonRun pythonRun = Validate(
                label + " " + mode + " python",
                sourceGraph,
                Path.Combine(
                    pythonFolder.DirectoryPath, validationGraph.Replace('/', Path.DirectorySeparatorChar)),
                dimensions,
                cancellationToken);
            VenvPythonRun managedRun = Validate(
                label + " " + mode + " managed",
                sourceGraph,
                Path.Combine(
                    managedFolder.DirectoryPath, validationGraph.Replace('/', Path.DirectorySeparatorChar)),
                dimensions,
                cancellationToken);

            foreach (string measurement in ValidationMeasurements)
            {
                managedRun.Value(measurement).Should().Be(
                    pythonRun.Value(measurement),
                    "the two engines' results must measure the same: " + measurement);
            }

            managedRun.Value("finite").Should().Be("True");
            managedRun.Value("shapes-match").Should().Be("True");
        }
        finally
        {
            await ExportTestStore.RemoveAsync(store, pythonName, cancellationToken);
            await ExportTestStore.RemoveAsync(store, managedName, cancellationToken);
        }
    }

    /// <summary>
    /// Everything two graphs differ in, with any weights kept beside them read in first so that a graph written in
    /// one piece and a graph written with a file of weights are compared as the models they are.
    /// </summary>
    /// <param name="expectedPath">What the Python engine wrote.</param>
    /// <param name="actualPath">What the managed engine wrote.</param>
    /// <param name="cancellationToken">A token that cancels the reads.</param>
    /// <returns>The differences, empty when the two are the same model.</returns>
    private static async Task<IReadOnlyList<string>> DifferencesAsync(
        string expectedPath, string actualPath, CancellationToken cancellationToken)
    {
        OnnxModel expected = await OnnxModel.ReadAsync(expectedPath, cancellationToken);
        await expected.LoadExternalDataAsync(cancellationToken);
        OnnxModel actual = await OnnxModel.ReadAsync(actualPath, cancellationToken);
        await actual.LoadExternalDataAsync(cancellationToken);
        return OnnxModelComparison.Compare(expected.Proto, actual.Proto);
    }

    /// <summary>
    /// Whether two graphs were written the same way: one file each, or a file of weights each.
    /// </summary>
    /// <param name="expectedPath">What the Python engine wrote.</param>
    /// <param name="actualPath">What the managed engine wrote.</param>
    /// <returns><see langword="true"/> when both have a file of weights or neither does.</returns>
    private static bool SameLayout(string expectedPath, string actualPath)
        => File.Exists(expectedPath + ".data") == File.Exists(actualPath + ".data");

    /// <summary>
    /// Whether one of a bundle's files is a graph.
    /// </summary>
    /// <param name="path">The file's path as the bundle spells it.</param>
    /// <returns><see langword="true"/> for an <c>.onnx</c> file.</returns>
    private static bool IsGraph(string path)
        => path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes what each engine wrote for one graph where the run's output keeps it.
    /// </summary>
    /// <param name="label">What is being compared.</param>
    /// <param name="mode">The mode that ran.</param>
    /// <param name="graph">The graph's path as the bundle spells it.</param>
    /// <param name="expectedPath">What the Python engine wrote.</param>
    /// <param name="actualPath">What the managed engine wrote.</param>
    private void ReportGraph(
        string label, ReduceMode mode, string graph, string expectedPath, string actualPath)
        => _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "file: {0} {1} {2}: python {3} (+{4}) bytes, managed {5} (+{6}) bytes",
            label,
            mode,
            graph,
            new FileInfo(expectedPath).Length,
            SideFileBytes(expectedPath),
            new FileInfo(actualPath).Length,
            SideFileBytes(actualPath)));

    /// <summary>
    /// The size of the file of weights beside a graph.
    /// </summary>
    /// <param name="path">The graph's path.</param>
    /// <returns>The side file's size, or 0 when there is none.</returns>
    private static long SideFileBytes(string path)
        => File.Exists(path + ".data") ? new FileInfo(path + ".data").Length : 0L;

    /// <summary>
    /// Runs the validation script over one reduced graph and writes everything it printed to the run's output.
    /// </summary>
    /// <param name="label">What is being validated.</param>
    /// <param name="sourcePath">The graph as it was.</param>
    /// <param name="reducedPath">The graph as one engine made it.</param>
    /// <param name="dimensions">The shapes to run at.</param>
    /// <param name="cancellationToken">A token that cancels the wait for the child process.</param>
    /// <returns>What the script printed.</returns>
    private VenvPythonRun Validate(
        string label,
        string sourcePath,
        string reducedPath,
        IReadOnlyList<string> dimensions,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "--fp32", sourcePath,
            "--reduced", reducedPath,
            "--label", label,
            "--dims"
        };
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
        return run;
    }
}

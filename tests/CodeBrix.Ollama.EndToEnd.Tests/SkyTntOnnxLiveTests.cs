using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using CodeBrix.Ollama.ModelManager.Tests;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The managed ONNX interpreter against onnxruntime on the REAL published graphs: one step of each, and then
/// a second step whose past is the first step's present.
/// </summary>
/// <remarks>
/// <para>
/// THE SUBJECTS ARE A PUBLISHER'S OWN FILES, unconverted: the two graphs of a MIDI-generating decoder, 822 MB
/// and 117 MB of 32-bit floats, 954 and 271 nodes, written against opset 14. They are pulled into the
/// test-model cache once and kept there.
/// </para>
/// <para>
/// THE STORE AND THE RUNNER ARE CONNECTED BY THIS TEST AND BY NOTHING ELSE. The store resolves the bundle to
/// blob paths - files held under digests rather than under the publisher's names - and the test projects the
/// pairs into the (logical name to path) form the runner's own API takes. Neither library names a type of the
/// other; this project is the only one that names both.
/// </para>
/// <para>
/// THE BAR is a largest difference below 1e-4 of the tensor's own scale on every output, and the same
/// greedy choice on every row of logits. Both are measured and both are written into the test's output along
/// with the time per step at one thread and at eight, and the process's peak resident memory.
/// </para>
/// </remarks>
public sealed class SkyTntOnnxLiveTests
{
    /// <summary>The bar the plan sets for a full-precision step against onnxruntime.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>The graph that turns an event's hidden state into its tokens, 117 MB.</summary>
    private const string TokenGraph = "onnx/model_token.onnx";

    /// <summary>The graph that turns a window of events into hidden states, 822 MB.</summary>
    private const string BaseGraph = "onnx/model_base.onnx";

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class.</summary>
    /// <param name="output">Where the measured numbers are written.</param>
    public SkyTntOnnxLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// One step, and a cached second step, of the token graph agree with onnxruntime.
    /// </summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_token_graph() =>
        await CompareAsync(TokenGraph, "token", TestContext.Current.CancellationToken);

    /// <summary>
    /// One step, and a cached second step, of the base graph agree with onnxruntime.
    /// </summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task Run_matches_onnxruntime_on_the_base_graph() =>
        await CompareAsync(BaseGraph, "base", TestContext.Current.CancellationToken);

    private async Task CompareAsync(string graph, string kind, CancellationToken cancellationToken)
    {
        //Arrange
        var virtualEnvironment = TestGates.RequireVirtualEnvironment();
        using var store = EndToEndStore.Open();
        var name = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.SkyTntMidiModelTv2oMediumOnnxOnly, cancellationToken);
        var resolved = await store.ResolveAsync(name, cancellationToken);

        // The consumer's own two lines: the store says which blob holds which of the publisher's files, and
        // the runner is handed those pairs. Nothing else connects the two libraries.
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in resolved.Files) files[file.Name] = file.BlobPath;
        files.Should().ContainKey(graph);

        using var work = new TempWorkDirectory();
        var oracle = OnnxOracle.Run(
            virtualEnvironment, files[graph], work.DirectoryPath, kind, 8, null, cancellationToken);
        oracle.Steps.Should().HaveCount(2);

        //Act and assert
        foreach (var threads in new[] { 1, 8 })
        {
            // The memory measurement is made around the FIRST load, with the high-water mark forgotten and
            // what the last test left behind collected first, so the number reported is one load's own peak
            // rather than everything this process has ever held.
            bool measuring = threads == 1;
            long before = 0;
            if (measuring)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                before = ProcessMemory.Resident();
                ProcessMemory.ResetPeak();
            }

            var loading = Stopwatch.StartNew();
            await using var model = await OnnxModel.LoadFromFilesAsync(
                files, graph, new OnnxRunnerOptions { Threads = threads }, cancellationToken);
            loading.Stop();

            if (measuring)
            {
                _output.WriteLine(
                    graph + ": " + model.Metadata.Operators.Count.ToString(CultureInfo.InvariantCulture)
                    + " distinct operators, " + model.Metadata.Inputs.Count.ToString(CultureInfo.InvariantCulture)
                    + " inputs, " + model.Metadata.Outputs.Count.ToString(CultureInfo.InvariantCulture)
                    + " outputs, opset " + Opset(model));
                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture, "{0}: loaded in {1:F0} ms",
                    graph, loading.Elapsed.TotalMilliseconds));
            }

            foreach (var step in oracle.Steps)
            {
                // The first run of a step is the one that is checked; the second is the one that is timed,
                // so that a cold start - the first pass through code the runtime has not compiled yet - is
                // not reported as the cost of a step.
                var produced = model.Run(step.Inputs);
                Agree(graph, threads, step, produced);

                var stopwatch = Stopwatch.StartNew();
                model.Run(step.Inputs);
                stopwatch.Stop();

                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} step {1} at {2} thread(s): managed {3:F1} ms, onnxruntime {4:F1} ms at {5} threads",
                    graph, step.Index, threads, stopwatch.Elapsed.TotalMilliseconds, step.Milliseconds,
                    oracle.Threads));
            }

            if (measuring)
            {
                long peak = ProcessMemory.PeakResident();
                long after = ProcessMemory.Resident();

                // What the LOADED model itself costs, as against what the load has not yet handed back. The
                // reading and the conversion leave a model's worth of garbage behind them, and the collector
                // returns it when it is next asked or when the machine wants it; the difference between the
                // two numbers below is that garbage and nothing else.
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long settled = ProcessMemory.Resident();

                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: resident memory {1} before the load, {2} after it, {3} at its highest,"
                    + " {4} once the load's own garbage is collected",
                    graph, ProcessMemory.Mebibytes(before), ProcessMemory.Mebibytes(after),
                    ProcessMemory.Mebibytes(peak), ProcessMemory.Mebibytes(settled)));
            }
        }
    }

    private void Agree(
        string graph, int threads, OnnxOracleStep step, IReadOnlyDictionary<string, OnnxTensor> produced)
    {
        produced.Should().HaveCount(step.Outputs.Count);

        double worst = 0;
        string worstName = null;
        int disagreements = 0;
        int rows = 0;

        foreach (var expected in step.Outputs)
        {
            produced.Should().ContainKey(expected.Key);
            var actual = produced[expected.Key];
            var difference = OnnxAgreement.RelativeDifference(expected.Value, actual);
            if (difference > worst)
            {
                worst = difference;
                worstName = expected.Key;
            }

            var (wrong, counted) = OnnxAgreement.Argmax(expected.Value, actual);
            disagreements += wrong;
            rows += counted;
        }

        _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0} step {1} at {2} thread(s): worst relative difference {3} on '{4}', {5} of {6} greedy"
            + " choices differ",
            graph, step.Index, threads, OnnxAgreement.Number(worst), worstName, disagreements, rows));

        worst.Should().BeLessThan(
            Tolerance,
            graph + " step " + step.Index.ToString(CultureInfo.InvariantCulture) + " at "
            + threads.ToString(CultureInfo.InvariantCulture) + " thread(s), worst on '" + worstName + "'");
        disagreements.Should().Be(0);
    }

    private static string Opset(IOnnxModel model)
    {
        foreach (var import in model.Metadata.Opsets)
        {
            if (string.IsNullOrEmpty(import.Domain)) return import.Version.ToString(CultureInfo.InvariantCulture);
        }

        return "unstated";
    }
}

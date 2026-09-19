using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Threading;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// One subject held to onnxruntime: a step and a cached step of one graph, run by both engines on the SAME
/// numbers, at one thread and at eight.
/// </summary>
/// <remarks>
/// <para>
/// THE ORACLE GOES FIRST AND WRITES DOWN WHAT IT FED THE GRAPH. The managed engine is then given exactly
/// those tensors, so the two are compared on identical inputs rather than on two runs that each made up their
/// own - which matters most for the second step, whose past is the first step's present.
/// </para>
/// <para>
/// THE ANSWER AND THE CACHE ARE COUNTED SEPARATELY. A graph's <c>present</c> outputs are the key-value cache,
/// which the driver hands straight back in at the next step; everything else is what the model is being asked
/// for. They are reported apart because the greedy choice - the thing a generation actually turns on - is a
/// property of the ANSWER, and the largest element of a row of sixty-four cached values is not a choice
/// anybody makes.
/// </para>
/// <para>
/// THE BAR DEPENDS ON WHAT THE GRAPH QUANTIZES, and the three levels are measured rather than guessed; see
/// <see cref="OnnxSubjectKind"/>.
/// </para>
/// </remarks>
public static class OnnxSubjectComparison
{
    /// <summary>The plan's bar for a graph whose weights are full-precision floats.</summary>
    public const double FullPrecisionTolerance = 1e-4;

    /// <summary>The plan's bar for a graph whose WEIGHTS were quantized and whose activations were not.</summary>
    public const double QuantizedTolerance = 1e-2;

    /// <summary>
    /// The bar for a graph that quantizes its ACTIVATIONS as well, which is a different problem and a
    /// measured number rather than a chosen one.
    /// </summary>
    /// <remarks>
    /// Quantizing an activation makes a step of these models NUMERICALLY CHAOTIC, and that is a property of
    /// the graph rather than of either engine. The scale of a whole tensor is worked out from its largest and
    /// smallest element, so a difference in the LAST BIT of one element moves the scale, which moves every
    /// value that was sitting halfway between two integers by a whole count, and twelve layers multiply that
    /// up. It is measurable from one side alone: this engine's own two arithmetic paths - which differ only in
    /// the order they add floats in - disagree with EACH OTHER by up to 3.7e-2 on these graphs, while
    /// agreeing to 1e-6 on every graph that quantizes only its weights. The bar is set above that, with
    /// headroom; holding the comparison to anything tighter would be pinning noise.
    /// </remarks>
    public const double DynamicActivationTolerance = 6e-2;

    /// <summary>
    /// Runs the oracle over one graph, then the managed engine over the same inputs, and holds the two to each
    /// other.
    /// </summary>
    /// <param name="output">Where the measured numbers are written.</param>
    /// <param name="virtualEnvironment">The virtual environment whose interpreter runs the oracle.</param>
    /// <param name="subject">What to call this subject in the report.</param>
    /// <param name="files">The bundle's files, as (logical name to path) pairs.</param>
    /// <param name="graph">The logical name of the graph to run.</param>
    /// <param name="kind">Which step shapes the oracle should build: <c>token</c>, <c>base</c> or <c>causal</c>.</param>
    /// <param name="subjectKind">What the graph quantizes, which settles the bar and the greedy-choice rule.</param>
    /// <param name="work">A directory the oracle may write into.</param>
    /// <param name="oracleStrippedCopy">
    /// Where the oracle should write a copy of the graph with <c>accuracy_level</c> taken off every
    /// <c>MatMulNBits</c> node and run that instead, or <see langword="null"/> to run the graph as it is.
    /// THE MANAGED ENGINE ALWAYS RUNS THE ORIGINAL, whatever this says - it reads the attribute and ignores
    /// it, so stripping it would make no difference to its answer - and that asymmetry is the whole point of
    /// the comparison this enables.
    /// </param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>What the run measured.</returns>
    public static OnnxSubjectResult Run(
        ITestOutputHelper output,
        string virtualEnvironment,
        string subject,
        IReadOnlyDictionary<string, string> files,
        string graph,
        string kind,
        OnnxSubjectKind subjectKind,
        string work,
        CancellationToken cancellationToken,
        string oracleStrippedCopy = null)
    {
        files.Should().ContainKey(graph);
        OnnxOracleRun oracle = OnnxOracle.Run(
            virtualEnvironment, files[graph], work, kind, 8, oracleStrippedCopy, cancellationToken);
        oracle.Steps.Should().HaveCount(2);

        double tolerance = Tolerance(subjectKind);
        OnnxSubjectResult measured = new OnnxSubjectResult(subject, graph);

        foreach (int threads in new[] { 1, 8 })
        {
            bool measuring = threads == 1;
            if (measuring)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                ProcessMemory.ResetPeak();
            }

            Stopwatch loading = Stopwatch.StartNew();
            using IOnnxModel model = OnnxModel.LoadFromFilesAsync(
                    files, graph, new OnnxRunnerOptions { Threads = threads }, cancellationToken)
                .GetAwaiter().GetResult();
            loading.Stop();

            if (measuring)
            {
                measured.LoadMilliseconds = loading.Elapsed.TotalMilliseconds;
                output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} / {1}: {2} distinct operators, loaded in {3:F0} ms",
                    subject, graph, model.Metadata.Operators.Count, loading.Elapsed.TotalMilliseconds));
            }

            foreach (OnnxOracleStep step in oracle.Steps)
            {
                IReadOnlyDictionary<string, OnnxTensor> produced = model.Run(step.Inputs);
                Agree(output, measured, subject, graph, threads, step, produced, tolerance, subjectKind);

                Stopwatch stopwatch = Stopwatch.StartNew();
                model.Run(step.Inputs);
                stopwatch.Stop();
                measured.Record(threads, step.Index, stopwatch.Elapsed.TotalMilliseconds, step.Milliseconds);

                output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} / {1} step {2} at {3} thread(s): managed {4:F1} ms, onnxruntime {5:F1} ms at {6}"
                    + " threads",
                    subject, graph, step.Index, threads, stopwatch.Elapsed.TotalMilliseconds,
                    step.Milliseconds, oracle.Threads));
            }

            if (measuring)
            {
                long peak = ProcessMemory.PeakResident();
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                measured.PeakResidentBytes = peak;
                measured.SettledResidentBytes = ProcessMemory.Resident();
                output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} / {1}: resident memory {2} at its highest, {3} once the load's own garbage is"
                    + " collected",
                    subject, graph, ProcessMemory.Mebibytes(measured.PeakResidentBytes),
                    ProcessMemory.Mebibytes(measured.SettledResidentBytes)));
            }
        }

        return measured;
    }

    /// <summary>The largest relative difference a subject of this kind is allowed.</summary>
    /// <param name="kind">What the graph quantizes.</param>
    /// <returns>The bar.</returns>
    public static double Tolerance(OnnxSubjectKind kind) => kind switch
    {
        OnnxSubjectKind.FullPrecision => FullPrecisionTolerance,
        OnnxSubjectKind.QuantizedWeights => QuantizedTolerance,
        _ => DynamicActivationTolerance,
    };

    private static void Agree(
        ITestOutputHelper output,
        OnnxSubjectResult measured,
        string subject,
        string graph,
        int threads,
        OnnxOracleStep step,
        IReadOnlyDictionary<string, OnnxTensor> produced,
        double tolerance,
        OnnxSubjectKind subjectKind)
    {
        produced.Should().HaveCount(step.Outputs.Count);

        double worstAnswer = 0;
        double worstCache = 0;
        string worstName = null;
        int answerWrong = 0;
        int answerRows = 0;
        int cacheWrong = 0;
        int cacheRows = 0;

        foreach (KeyValuePair<string, OnnxTensor> expected in step.Outputs)
        {
            produced.Should().ContainKey(expected.Key);
            double difference = OnnxAgreement.RelativeDifference(expected.Value, produced[expected.Key]);
            (int wrong, int counted) = OnnxAgreement.Argmax(expected.Value, produced[expected.Key]);

            // A `present` output is the key-value cache the next step is given back; anything else is what
            // the model was asked for.
            if (expected.Key.StartsWith("present", StringComparison.Ordinal))
            {
                if (difference > worstCache) worstCache = difference;
                cacheWrong += wrong;
                cacheRows += counted;
            }
            else
            {
                if (difference > worstAnswer || worstName == null)
                {
                    worstAnswer = difference;
                    worstName = expected.Key;
                }

                answerWrong += wrong;
                answerRows += counted;
            }
        }

        measured.Note(step.Index, worstAnswer, worstCache, worstName, answerWrong, answerRows, cacheWrong);

        output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0} / {1} step {2} at {3} thread(s): answer {4} on '{5}' ({6} of {7} greedy choices differ),"
            + " cache {8} ({9} rows differ)",
            subject, graph, step.Index, threads, OnnxAgreement.Number(worstAnswer), worstName, answerWrong,
            answerRows, OnnxAgreement.Number(worstCache), cacheWrong));

        string where = subject + " / " + graph + " step "
            + step.Index.ToString(CultureInfo.InvariantCulture) + " at "
            + threads.ToString(CultureInfo.InvariantCulture) + " thread(s)";

        worstAnswer.Should().BeLessThan(tolerance, where + ", worst on '" + worstName + "'");
        worstCache.Should().BeLessThan(tolerance, where + ", the key-value cache");

        if (subjectKind != OnnxSubjectKind.LowerPrecisionByRequest)
        {
            answerWrong.Should().Be(0, where + ", the greedy choice");
        }
    }
}

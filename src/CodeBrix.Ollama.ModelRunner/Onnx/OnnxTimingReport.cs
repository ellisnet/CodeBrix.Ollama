using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Opt-in per-node timing for a loaded ONNX graph. The hot path stores ticks in arrays; JSON is written only
/// when the session is disposed. Set CODEBRIX_OLLAMA_ONNX_TIMING_DIR to a directory to enable it.
/// </summary>
internal sealed class OnnxTimingReport
{
    internal const string DirectoryEnvironmentVariable = "CODEBRIX_OLLAMA_ONNX_TIMING_DIR";

    private readonly OnnxExecutionPlan _plan;
    private readonly string _directory;
    private readonly long[] _nodeTicks;
    private readonly Dictionary<int, long[]> _bucketNodeTicks = new Dictionary<int, long[]>();
    private readonly Dictionary<int, int> _bucketRuns = new Dictionary<int, int>();
    private readonly List<RunEntry> _runs = new List<RunEntry>();
    private long _runTicks;
    private long _kernelTicks;

    private OnnxTimingReport(OnnxExecutionPlan plan, string directory)
    {
        _plan = plan;
        _directory = directory;
        _nodeTicks = new long[plan.Nodes.Length];
    }

    internal static OnnxTimingReport FromEnvironment(OnnxExecutionPlan plan)
    {
        string directory = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable);
        return string.IsNullOrWhiteSpace(directory) ? null : new OnnxTimingReport(plan, directory);
    }

    internal static int ContextLength(IReadOnlyDictionary<string, OnnxTensor> inputs)
    {
        if (inputs.TryGetValue("past_key_values.0.key", out OnnxTensor cache)
            && cache?.Shape.Count > 2 && cache.Shape[2] >= 0 && cache.Shape[2] <= int.MaxValue)
        {
            return (int)cache.Shape[2];
        }

        return -1;
    }

    internal void Node(int index, int contextLength, long ticks)
    {
        _nodeTicks[index] += ticks;
        int bucket = contextLength < 0 ? -1 : contextLength / 100;
        if (!_bucketNodeTicks.TryGetValue(bucket, out long[] byNode))
        {
            byNode = new long[_nodeTicks.Length];
            _bucketNodeTicks.Add(bucket, byNode);
        }

        byNode[index] += ticks;
    }

    internal void Run(int contextLength, long runTicks, long kernelTicks)
    {
        _runs.Add(new RunEntry(contextLength, runTicks, kernelTicks));
        _runTicks += runTicks;
        _kernelTicks += kernelTicks;
        int bucket = contextLength < 0 ? -1 : contextLength / 100;
        _bucketRuns.TryGetValue(bucket, out int count);
        _bucketRuns[bucket] = count + 1;
    }

    internal void Write()
    {
        Directory.CreateDirectory(_directory);
        string file = Path.Combine(
            _directory,
            "onnx-timing-" + Environment.ProcessId + "-" + _plan.Nodes.Length + "-"
            + Guid.NewGuid().ToString("N") + ".json");

        var report = new
        {
            formatVersion = 1,
            graphName = _plan.Metadata.GraphName,
            nodeCount = _plan.Nodes.Length,
            runCount = _runs.Count,
            contextBucketSize = 100,
            totalRunMs = Milliseconds(_runTicks),
            totalKernelMs = Milliseconds(_kernelTicks),
            nodes = _plan.Nodes.Select((node, index) => new
            {
                index,
                opType = node.OpType,
                name = node.Name,
                totalMs = Milliseconds(_nodeTicks[index]),
            }).ToArray(),
            contextBuckets = _bucketNodeTicks.OrderBy(pair => pair.Key).Select(pair => new
            {
                firstContext = pair.Key < 0 ? -1 : pair.Key * 100,
                lastContext = pair.Key < 0 ? -1 : (pair.Key * 100) + 99,
                runs = _bucketRuns.GetValueOrDefault(pair.Key),
                nodeMs = pair.Value.Select(Milliseconds).ToArray(),
            }).ToArray(),
            runs = _runs.Select((run, index) => new
            {
                index,
                contextLength = run.ContextLength,
                totalMs = Milliseconds(run.TotalTicks),
                kernelMs = Milliseconds(run.KernelTicks),
            }).ToArray(),
        };

        File.WriteAllText(file, JsonSerializer.Serialize(report));
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private readonly struct RunEntry
    {
        internal RunEntry(int contextLength, long totalTicks, long kernelTicks)
        {
            ContextLength = contextLength;
            TotalTicks = totalTicks;
            KernelTicks = kernelTicks;
        }

        internal int ContextLength { get; }

        internal long TotalTicks { get; }

        internal long KernelTicks { get; }
    }
}

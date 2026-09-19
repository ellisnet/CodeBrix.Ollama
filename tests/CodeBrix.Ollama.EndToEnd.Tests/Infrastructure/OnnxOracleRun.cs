using System.Collections.Generic;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>A whole oracle run: the steps it recorded and how many threads onnxruntime used.</summary>
public sealed class OnnxOracleRun
{
    /// <summary>Builds a run.</summary>
    /// <param name="threads">How many threads onnxruntime was given.</param>
    /// <param name="steps">The steps, in order.</param>
    public OnnxOracleRun(int threads, IReadOnlyList<OnnxOracleStep> steps)
    {
        Threads = threads;
        Steps = steps;
    }

    /// <summary>How many threads onnxruntime was given.</summary>
    public int Threads { get; }

    /// <summary>The steps, in order.</summary>
    public IReadOnlyList<OnnxOracleStep> Steps { get; }
}

using System.Collections.Generic;
using CodeBrix.Ollama.ModelRunner;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// One step of a decoder as onnxruntime ran it: exactly what it was fed, exactly what it produced, and how
/// long it took.
/// </summary>
public sealed class OnnxOracleStep
{
    /// <summary>Builds a step.</summary>
    /// <param name="index">Which step it is: nought for the first, one for the cached second.</param>
    /// <param name="inputs">What onnxruntime was fed.</param>
    /// <param name="outputs">What it produced.</param>
    /// <param name="milliseconds">How long the run took.</param>
    public OnnxOracleStep(
        int index,
        IReadOnlyDictionary<string, OnnxTensor> inputs,
        IReadOnlyDictionary<string, OnnxTensor> outputs,
        double milliseconds)
    {
        Index = index;
        Inputs = inputs;
        Outputs = outputs;
        Milliseconds = milliseconds;
    }

    /// <summary>Which step it is: nought for the first, one for the cached second.</summary>
    public int Index { get; }

    /// <summary>What onnxruntime was fed, which is what the managed engine is fed as well.</summary>
    public IReadOnlyDictionary<string, OnnxTensor> Inputs { get; }

    /// <summary>What onnxruntime produced.</summary>
    public IReadOnlyDictionary<string, OnnxTensor> Outputs { get; }

    /// <summary>How long onnxruntime's own run took.</summary>
    public double Milliseconds { get; }
}

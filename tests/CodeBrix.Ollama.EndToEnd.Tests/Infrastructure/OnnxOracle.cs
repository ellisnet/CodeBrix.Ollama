using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using CodeBrix.Ollama.ModelRunner;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// Runs a real ONNX decoder through onnxruntime, in the virtual environment's own interpreter, and reads back
/// exactly what it fed the graph and exactly what it produced.
/// </summary>
/// <remarks>
/// <para>
/// The point of reading the INPUTS back, rather than building them on both sides, is that the two engines are
/// then compared on the same numbers rather than on two runs that each made up their own. It matters most for
/// the cached second step, whose past is the first step's present: whichever engine produced that cache, both
/// are given the SAME one.
/// </para>
/// <para>
/// Nothing here goes through the library under test.
/// </para>
/// </remarks>
public static class OnnxOracle
{
    /// <summary>The script's file name in this project's Python folder.</summary>
    public const string ScriptName = "onnx_oracle.py";

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Runs the oracle over one graph and reads back both steps.</summary>
    /// <param name="virtualEnvironment">The virtual environment whose interpreter runs the script.</param>
    /// <param name="modelPath">The graph file's path.</param>
    /// <param name="workDirectory">Where the script writes what it fed and what it produced.</param>
    /// <param name="kind">Which of the two SkyTNT graphs it is: <c>token</c> or <c>base</c>.</param>
    /// <param name="threads">How many threads onnxruntime is given.</param>
    /// <param name="strippedCopy">
    /// Where to write a copy of the graph with <c>accuracy_level</c> taken off every <c>MatMulNBits</c> node,
    /// which the oracle then runs INSTEAD of the original, or <see langword="null"/> to run the original as
    /// it is. It must be a path beside the model, because a graph that keeps its weights in a side file names
    /// that file relatively and onnxruntime will not follow such a name out of the model's own directory.
    /// </param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>The steps it recorded.</returns>
    /// <exception cref="InvalidOperationException">The script did not finish, or did not say it succeeded.</exception>
    public static OnnxOracleRun Run(
        string virtualEnvironment,
        string modelPath,
        string workDirectory,
        string kind,
        int threads,
        string strippedCopy,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "--model", modelPath,
            "--work", workDirectory,
            "--kind", kind,
            "--threads", threads.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrEmpty(strippedCopy))
        {
            arguments.Add("--strip-accuracy-level");
            arguments.Add(strippedCopy);
        }

        var run = VenvPython.Run(virtualEnvironment, ScriptName, arguments.ToArray(), cancellationToken);

        if (!run.Exited || run.ExitCode != 0 || !run.Output.Contains("ORACLE OK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The ONNX oracle did not finish: " + run.Report());
        }

        var report = JsonSerializer.Deserialize<OnnxOracleReport>(
            File.ReadAllText(Path.Combine(workDirectory, "oracle.json")), Json);

        var steps = new List<OnnxOracleStep>();
        foreach (var step in report.Steps)
        {
            var folder = Path.Combine(workDirectory, "step" + step.Step.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            steps.Add(new OnnxOracleStep(
                step.Step,
                Read(Path.Combine(folder, "inputs")),
                Read(Path.Combine(folder, "outputs")),
                step.Milliseconds));
        }

        return new OnnxOracleRun(report.Threads, steps);
    }

    private static IReadOnlyDictionary<string, OnnxTensor> Read(string folder)
    {
        var manifest = JsonSerializer.Deserialize<OnnxTensorManifest>(
            File.ReadAllText(Path.Combine(folder, "manifest.json")), Json);

        var tensors = new Dictionary<string, OnnxTensor>(StringComparer.Ordinal);
        foreach (var tensor in manifest.Tensors)
        {
            var bytes = File.ReadAllBytes(Path.Combine(folder, tensor.File));
            tensors[tensor.Name] = tensor.Type switch
            {
                "float" => OnnxTensor.FromFloats(
                    MemoryMarshal.Cast<byte, float>(bytes).ToArray(), tensor.Shape),
                "int64" => OnnxTensor.FromInt64(
                    MemoryMarshal.Cast<byte, long>(bytes).ToArray(), tensor.Shape),
                "int32" => OnnxTensor.FromInt32(
                    MemoryMarshal.Cast<byte, int>(bytes).ToArray(), tensor.Shape),
                "bool" => OnnxTensor.FromBooleans(Booleans(bytes), tensor.Shape),
                _ => throw new InvalidOperationException(
                    "The oracle wrote '" + tensor.Name + "' as " + tensor.Type + ", which is not read here."),
            };
        }

        return tensors;
    }

    private static bool[] Booleans(byte[] bytes)
    {
        var values = new bool[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) values[i] = bytes[i] != 0;
        return values;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Finds and reads the checked-in ONNX operator oracles.
/// </summary>
/// <remarks>
/// Everything it touches was copied beside the test assembly by the project file, so a run of this suite
/// reaches nothing outside the repository - no network, no virtual environment, no model cache. The numbers
/// it hands out were produced by ONNX Runtime when a maintainer ran generate_fixtures.py by hand;
/// Fixtures/README.txt says so in full.
/// </remarks>
public static class OnnxFixtures
{
    private static readonly Lazy<OnnxFixtureIndex> Index = new Lazy<OnnxFixtureIndex>(ReadIndex);

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The folder the fixtures were copied into, beside the test assembly.</summary>
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Onnx", "Fixtures");

    /// <summary>The folder holding the graphs that must be refused.</summary>
    public static string RefusalDirectory => Path.Combine(Directory, "Refusals");

    /// <summary>Every case's name, sorted.</summary>
    public static IReadOnlyList<string> Names => Index.Value.Cases;

    /// <summary>Every graph that must be refused, with the text its refusal has to carry.</summary>
    public static IReadOnlyList<OnnxRefusalCase> Refusals => Index.Value.Refusals;

    /// <summary>Every case's name, as a theory's data.</summary>
    /// <returns>One row per case.</returns>
    public static TheoryData<string> AllCases()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (string name in Names) data.Add(name);
        return data;
    }

    /// <summary>
    /// The cases whose arithmetic is heavy enough for threading and kernel paths to show a difference, as a
    /// theory's data.
    /// </summary>
    /// <returns>One row per case.</returns>
    public static TheoryData<string> ArithmeticCases()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (string name in Names)
        {
            if (name.StartsWith("matmul", StringComparison.Ordinal)
                || name.StartsWith("subgraph", StringComparison.Ordinal)
                || name.StartsWith("softmax", StringComparison.Ordinal)
                || name.StartsWith("reduce", StringComparison.Ordinal))
            {
                data.Add(name);
            }
        }

        return data;
    }

    /// <summary>Every graph that must be refused, as a theory's data.</summary>
    /// <returns>One row per graph: its name and the text its refusal has to carry.</returns>
    public static TheoryData<string, string> AllRefusals()
    {
        TheoryData<string, string> data = new TheoryData<string, string>();
        foreach (OnnxRefusalCase refusal in Refusals) data.Add(refusal.Name, refusal.Expect);
        return data;
    }

    /// <summary>Reads one case: its manifest, its inputs and the outputs ONNX Runtime produced.</summary>
    /// <param name="name">The case's name.</param>
    /// <returns>The loaded case.</returns>
    public static OnnxFixtureCase Load(string name)
    {
        string folder = Path.Combine(Directory, name);
        OnnxFixtureManifest manifest = JsonSerializer.Deserialize<OnnxFixtureManifest>(
            File.ReadAllText(Path.Combine(folder, "case.json")), Json);

        return new OnnxFixtureCase(
            manifest,
            Path.Combine(folder, "model.onnx"),
            Read(folder, manifest.Inputs),
            Read(folder, manifest.Outputs));
    }

    /// <summary>The path of one graph that must be refused.</summary>
    /// <param name="name">Its name, without an extension.</param>
    /// <returns>The path.</returns>
    public static string RefusalPath(string name) => Path.Combine(RefusalDirectory, name + ".onnx");

    private static OnnxFixtureIndex ReadIndex() => JsonSerializer.Deserialize<OnnxFixtureIndex>(
        File.ReadAllText(Path.Combine(Directory, "cases.json")), Json);

    private static IReadOnlyDictionary<string, OnnxTensor> Read(string folder, OnnxFixtureTensor[] tensors)
    {
        Dictionary<string, OnnxTensor> read = new Dictionary<string, OnnxTensor>(StringComparer.Ordinal);
        foreach (OnnxFixtureTensor tensor in tensors)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, tensor.File));
            read[tensor.Name] = tensor.Type switch
            {
                "float" => OnnxTensor.FromFloats(MemoryMarshal.Cast<byte, float>(bytes).ToArray(), tensor.Shape),
                "int64" => OnnxTensor.FromInt64(MemoryMarshal.Cast<byte, long>(bytes).ToArray(), tensor.Shape),
                "int32" => OnnxTensor.FromInt32(MemoryMarshal.Cast<byte, int>(bytes).ToArray(), tensor.Shape),
                "bool" => OnnxTensor.FromBooleans(Booleans(bytes), tensor.Shape),
                _ => throw new InvalidOperationException(
                    "The fixture '" + tensor.Name + "' is of an element type this suite does not read: "
                    + tensor.Type + "."),
            };
        }

        return read;
    }

    private static bool[] Booleans(byte[] bytes)
    {
        bool[] values = new bool[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) values[i] = bytes[i] != 0;
        return values;
    }
}

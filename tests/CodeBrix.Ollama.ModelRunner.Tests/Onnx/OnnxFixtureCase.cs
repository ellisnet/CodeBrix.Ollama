using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// One fixture case, loaded: where its graph is, the tensors to feed it, and what ONNX Runtime produced.
/// </summary>
public sealed class OnnxFixtureCase
{
    /// <summary>Builds a loaded case.</summary>
    /// <param name="manifest">What <c>case.json</c> said.</param>
    /// <param name="modelPath">The graph file's path.</param>
    /// <param name="inputs">The tensors to feed the graph.</param>
    /// <param name="expected">What ONNX Runtime produced from them.</param>
    public OnnxFixtureCase(
        OnnxFixtureManifest manifest,
        string modelPath,
        IReadOnlyDictionary<string, OnnxTensor> inputs,
        IReadOnlyDictionary<string, OnnxTensor> expected)
    {
        Manifest = manifest;
        ModelPath = modelPath;
        Inputs = inputs;
        Expected = expected;
    }

    /// <summary>What <c>case.json</c> said.</summary>
    public OnnxFixtureManifest Manifest { get; }

    /// <summary>The case's name.</summary>
    public string Name => Manifest.Name;

    /// <summary>The graph file's path.</summary>
    public string ModelPath { get; }

    /// <summary>The tensors to feed the graph.</summary>
    public IReadOnlyDictionary<string, OnnxTensor> Inputs { get; }

    /// <summary>What ONNX Runtime produced from them.</summary>
    public IReadOnlyDictionary<string, OnnxTensor> Expected { get; }
}

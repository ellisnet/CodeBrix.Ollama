using System;
using System.IO;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Paths to the conformance assets linked in from llama-native-tools/test-vectors.
/// </summary>
public static class TestVectors
{
    /// <summary>The folder the linked assets are copied to beside the test assembly.</summary>
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "test-vectors");

    /// <summary>The tiny synthetic conformance model.</summary>
    public static string ConformanceModelPath => Path.Combine(Directory, "codebrix-conformance-tiny.gguf");

    /// <summary>The reference logits the conformance model must produce.</summary>
    public static string ExpectedPath => Path.Combine(Directory, "EXPECTED.txt");
}

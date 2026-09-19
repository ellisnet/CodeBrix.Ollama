using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.Ollama.Core.Tests;

/// <summary>
/// Finds the checked-in ONNX oracle fixtures beside the test assembly. This is the CODEC's half of the set:
/// the model files the codec reads and writes back. The quantizer's half - which modes each fixture was
/// quantized with - lives with the quantizer's tests, because nothing here quantizes anything.
/// </summary>
internal static class OnnxFixtureFiles
{
    /// <summary>The folder the fixtures are copied to.</summary>
    internal static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "Onnx", "Fixtures");

    /// <summary>The full path of one fixture.</summary>
    /// <param name="fileName">The fixture's file name.</param>
    /// <returns>The path.</returns>
    internal static string FullPath(string fileName) => Path.Combine(Directory, fileName);

    /// <summary>Every fixture file, model files and side files alike.</summary>
    /// <returns>The file names, ordered.</returns>
    internal static IReadOnlyList<string> All() =>
        System.IO.Directory.GetFiles(Directory)
            .Select(System.IO.Path.GetFileName)
            .Where(name => name.EndsWith(".onnx", StringComparison.Ordinal)
                || name.EndsWith(".onnx.data", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Every fixture model file.</summary>
    /// <returns>The file names, ordered.</returns>
    internal static IReadOnlyList<string> AllModels() =>
        All().Where(name => name.EndsWith(".onnx", StringComparison.Ordinal)).ToList();

    /// <summary>Whether a fixture file exists.</summary>
    /// <param name="fileName">The fixture's file name.</param>
    /// <returns><see langword="true"/> when the file is there.</returns>
    internal static bool Exists(string fileName) => File.Exists(FullPath(fileName));
}

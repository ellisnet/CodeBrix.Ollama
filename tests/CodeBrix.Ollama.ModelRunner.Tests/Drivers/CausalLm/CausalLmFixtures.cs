using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Finds and reads the checked-in text-driver fixtures: the tiny single-graph bundle, and what the published
/// Python tokenizer made of a corpus.
/// </summary>
/// <remarks>
/// Everything it touches was copied beside the test assembly by the project file, so a run of this suite
/// reaches nothing outside the repository - no network, no virtual environment, no model cache.
/// Drivers/CausalLm/Fixtures/README.txt says who made the files and how.
/// </remarks>
public static class CausalLmFixtures
{
    private static readonly Lazy<CausalLmTokenizerFixture> Cases =
        new Lazy<CausalLmTokenizerFixture>(ReadCases);

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The folder the fixtures were copied into.</summary>
    public static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "Drivers", "CausalLm", "Fixtures");

    /// <summary>The tiny bundle's directory.</summary>
    public static string TinyBundleDirectory => Path.Combine(Directory, "tiny-bundle");

    /// <summary>What the published Python tokenizer made of the corpus.</summary>
    public static CausalLmTokenizerFixture TokenizerCases => Cases.Value;

    /// <summary>Every tokenizer case's position in the fixture, as a theory's data.</summary>
    /// <returns>One row per case.</returns>
    public static TheoryData<int> AllTokenizerCases()
    {
        TheoryData<int> data = new TheoryData<int>();
        for (int i = 0; i < TokenizerCases.Cases.Count; i++) data.Add(i);
        return data;
    }

    /// <summary>The (logical file name to path) pairs of the tiny bundle, as a store would hand them over.</summary>
    /// <returns>The pairs.</returns>
    public static IReadOnlyDictionary<string, string> TinyBundleFiles()
    {
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in System.IO.Directory.EnumerateFiles(TinyBundleDirectory))
        {
            files[Path.GetFileName(path)] = path;
        }

        return files;
    }

    private static CausalLmTokenizerFixture ReadCases() =>
        JsonSerializer.Deserialize<CausalLmTokenizerFixture>(
            File.ReadAllText(Path.Combine(Directory, "tokenizer-cases.json")), Json);
}

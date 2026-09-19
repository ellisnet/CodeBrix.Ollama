using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Finds and reads the checked-in MIDI-driver fixtures: the tiny two-graph model, and the sampling masks the
/// publisher's own generation loop builds.
/// </summary>
/// <remarks>
/// Everything it touches was copied beside the test assembly by the project file, so a run of this suite
/// reaches nothing outside the repository - no network, no virtual environment, no model cache.
/// Drivers/SkyTnt/Fixtures/README.txt says who made the files and how.
/// </remarks>
public static class SkyTntFixtures
{
    private static readonly Lazy<SkyTntMaskFixture> Masks = new Lazy<SkyTntMaskFixture>(ReadMasks);

    //The script that wrote masks.json names its fields the way the publisher's own Python does, so the
    //reader is told to expect that spelling rather than the file being rewritten to suit this language.
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The folder the fixtures were copied into.</summary>
    public static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "Drivers", "SkyTnt", "Fixtures");

    /// <summary>The tiny bundle's directory.</summary>
    public static string TinyModelDirectory => Path.Combine(Directory, "tiny-model");

    /// <summary>The sampling masks the publisher's generation loop builds.</summary>
    public static SkyTntMaskFixture MaskFixture => Masks.Value;

    /// <summary>Every mask's name, as a theory's data.</summary>
    /// <returns>One row per mask.</returns>
    public static TheoryData<string> AllMasks()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (SkyTntMaskCase one in MaskFixture.Cases) data.Add(one.Name);
        return data;
    }

    /// <summary>One mask by name.</summary>
    /// <param name="name">Its name.</param>
    /// <returns>The mask.</returns>
    public static SkyTntMaskCase Mask(string name)
    {
        foreach (SkyTntMaskCase one in MaskFixture.Cases)
        {
            if (string.Equals(one.Name, name, StringComparison.Ordinal)) return one;
        }

        throw new InvalidOperationException("There is no mask fixture called '" + name + "'.");
    }

    /// <summary>The (logical file name to path) pairs of the tiny bundle, as a store would hand them over.</summary>
    /// <returns>The pairs.</returns>
    public static IReadOnlyDictionary<string, string> TinyModelFiles() => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["config.json"] = Path.Combine(TinyModelDirectory, "config.json"),
        ["onnx/model_base.onnx"] = Path.Combine(TinyModelDirectory, "onnx", "model_base.onnx"),
        ["onnx/model_token.onnx"] = Path.Combine(TinyModelDirectory, "onnx", "model_token.onnx"),
    };

    /// <summary>
    /// The tokenizer, built from the tiny bundle's description of itself - which states the same numbers the
    /// published bundle states.
    /// </summary>
    /// <returns>The tokenizer.</returns>
    internal static SkyTntTokenizer Tokenizer() => SkyTntTokenizer.FromConfiguration(Configuration());

    /// <summary>What the tiny bundle says about its tokenizer.</summary>
    /// <returns>The description, read from its own config.json.</returns>
    internal static SkyTntTokenizerConfiguration Configuration() =>
        SkyTntBundle.FromDirectory(TinyModelDirectory).Tokenizer;

    private static SkyTntMaskFixture ReadMasks() =>
        JsonSerializer.Deserialize<SkyTntMaskFixture>(
            File.ReadAllText(Path.Combine(Directory, "masks.json")), Json);
}

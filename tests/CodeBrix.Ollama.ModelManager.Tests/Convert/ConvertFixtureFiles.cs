using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Finds the checked-in conversion fixtures beside the test assembly and names the variants the oracle
/// comparison walks.
/// </summary>
/// <remarks>
/// Each variant is a folder holding a tiny synthetic Llama checkpoint, with the GGUF the inference engine's own
/// converter produced from it beside it. generate_fixtures.py records how they were made and is never run by the
/// tests.
/// </remarks>
internal static class ConvertFixtureFiles
{
    /// <summary>The folder the fixtures are copied to.</summary>
    internal static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "Convert", "Fixtures");

    /// <summary>The full path of one checkpoint folder.</summary>
    /// <param name="variant">The variant's folder name.</param>
    /// <returns>The path.</returns>
    internal static string CheckpointPath(string variant) => Path.Combine(Directory, variant);

    /// <summary>The full path of the oracle GGUF for one variant and output type.</summary>
    /// <param name="variant">The variant's folder name.</param>
    /// <param name="outputType">The output type the oracle was produced at.</param>
    /// <returns>The path.</returns>
    internal static string OraclePath(string variant, string outputType) =>
        Path.Combine(Directory, variant + "." + outputType + ".gguf");

    /// <summary>Every variant and the output types its oracle was produced at.</summary>
    /// <returns>One entry per oracle file.</returns>
    internal static IReadOnlyList<(string Variant, string OutputType, GgufOutputType Requested)> All() =>
        new[]
        {
            ("tinyllama-123k", "auto", GgufOutputType.Auto),
            ("tinyllama-123k", "f16", GgufOutputType.F16),
            ("tinyllama-123k", "f32", GgufOutputType.F32),
            ("tinyllamabin-123k", "auto", GgufOutputType.Auto),
            ("tinyllamagqa-115k", "auto", GgufOutputType.Auto),
            ("tinyllamatied-103k", "auto", GgufOutputType.Auto),
            ("tinyllamabias-124k", "auto", GgufOutputType.Auto),
            ("tinyllamaftt-123k", "auto", GgufOutputType.Auto),
            ("tinyllamafst-123k", "auto", GgufOutputType.Auto),
            ("tinyllamapad-124k", "auto", GgufOutputType.Auto),
            ("tinyllamabare-123k", "auto", GgufOutputType.Auto),
            ("tinyllamasp-132k", "auto", GgufOutputType.Auto),
            ("tinyllamasplit-123k", "auto", GgufOutputType.Auto),
            ("tinyllamabinsplit-123k", "auto", GgufOutputType.Auto),
        };

    /// <summary>The variant whose weights are held as safetensors, and its pickle twin.</summary>
    internal static (string Safetensors, string Pickle) EquivalentPair =>
        ("tinyllama-123k", "tinyllamabin-123k");

    /// <summary>
    /// The variant whose tokenizer configuration declares no special token at all - MuPT's shape, where the
    /// tokenizer class names them in the publisher's own Python. Its oracle therefore writes the four tokens
    /// as ORDINARY tokens, which is what <see cref="ConvertOptions.AddedSpecialTokens"/> exists to correct.
    /// </summary>
    internal const string UndeclaredSpecialsVariant = "tinyllamabare-123k";

    /// <summary>The contents of the four tokens that variant's files do not declare, at their identifiers.</summary>
    internal static IReadOnlyList<string> UndeclaredSpecialTokens =>
        new[] { "<pad>", "<unk>", "<bos>", "<eos>" };

    /// <summary>The variant whose tokenizer is a SentencePiece <c>tokenizer.model</c>.</summary>
    internal const string SentencePieceVariant = "tinyllamasp-132k";

    /// <summary>
    /// The two variants whose weights are split over three shards with an index file beside them: the
    /// safetensors one, then the PyTorch zip-pickle one.
    /// </summary>
    internal static (string Safetensors, string Pickle) ShardedPair =>
        ("tinyllamasplit-123k", "tinyllamabinsplit-123k");
}

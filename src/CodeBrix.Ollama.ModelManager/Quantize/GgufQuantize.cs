using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What quantizing a stored model knows that is not about the store: which sources it will take, how the type
/// the caller names becomes a tag, and the settings the result's provenance records.
/// </summary>
/// <remarks>
/// Everything here works on what a model's layers already say or on what the caller passed, so a source that
/// cannot be quantized and a type that cannot be spelled are both refused before a byte is written.
/// </remarks>
internal static class GgufQuantize
{
    /// <summary>The name the quantized file is written under inside the working folder.</summary>
    internal const string OutputFileName = "model.gguf";

    /// <summary>The status reported while the caller's quantizer is running.</summary>
    internal const string QuantizingStatus = "quantizing";

    /// <summary>The longest a type may be spelled, which is what a model name allows a tag.</summary>
    private const int MaximumTypeLength = 128;

    /// <summary>
    /// The type as it is written into a name and a provenance record: trimmed and lower-cased, and refused
    /// outright when it holds anything a tag cannot.
    /// </summary>
    /// <param name="type">The type the caller named.</param>
    /// <returns>The normalized spelling.</returns>
    /// <exception cref="ArgumentException">The type is not set, or holds a character a tag cannot.</exception>
    /// <remarks>
    /// The store never interprets the type: what it means is the quantizer's business, and a store that knew
    /// the engine's list of types would be a store that goes out of date every time that list grows. All it
    /// insists on is that the string can be part of a name, because it is about to become part of one.
    /// </remarks>
    internal static string NormalizeType(string type)
    {
        string trimmed = (type ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                "A quantization needs the name of the type it produces, for example \"q4_k_m\"; set Type in"
                    + " the options.",
                nameof(type));
        }

        if (trimmed.Length > MaximumTypeLength)
        {
            throw new ArgumentException(
                "The quantization type '" + trimmed + "' is longer than a model name's tag may be.",
                nameof(type));
        }

        string lowered = trimmed.ToLowerInvariant();
        for (int i = 0; i < lowered.Length; i++)
        {
            char c = lowered[i];
            bool alphanumeric = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            if (alphanumeric || (i > 0 && (c == '-' || c == '.')))
            {
                continue;
            }

            throw new ArgumentException(
                "The quantization type '" + trimmed + "' cannot be part of a model name: a tag is made of"
                    + " letters, digits and underscores, with dashes and dots after the first character.",
                nameof(type));
        }

        return lowered;
    }

    /// <summary>
    /// The tag a quantized model takes: the conversion's own tag with the type appended, so that a name says
    /// what is inside the file.
    /// </summary>
    /// <param name="normalizedType">The type, as <see cref="NormalizeType"/> spells it.</param>
    /// <returns>The tag.</returns>
    internal static string TagFor(string normalizedType)
        => GgufConvert.GgufTag + "-" + normalizedType;

    /// <summary>
    /// Refuses a source that is not a single-file GGUF model, naming what it is instead.
    /// </summary>
    /// <param name="layers">The source model's decoded layers.</param>
    /// <param name="sourceName">The source model, spelled as it is stored.</param>
    /// <exception cref="InvalidOperationException">The model is a bundle, or carries no weights.</exception>
    /// <exception cref="NotSupportedException">The model's weights are split over several files.</exception>
    internal static void RequireGgufModel(ModelLayerReader layers, string sourceName)
    {
        if (layers.BundleFiles.Count > 0)
        {
            throw new InvalidOperationException(
                "The model " + sourceName + " is a publisher file tree rather than a GGUF model, so there is"
                    + " nothing to quantize. A checkpoint becomes a GGUF model through ConvertToGgufAsync,"
                    + " and that model is what a quantization reads.");
        }

        if (layers.ModelPath == null)
        {
            throw new InvalidOperationException(
                "The model " + sourceName + " carries no weights layer, so there is nothing to quantize.");
        }

        if (layers.ModelShardPaths.Count > 0)
        {
            throw new NotSupportedException(
                "The model " + sourceName + " keeps its weights in " + (layers.ModelShardPaths.Count + 1)
                    + " files. This version quantizes a model held in one file.");
        }
    }

    /// <summary>
    /// The settings a quantized model records about the quantization that produced it.
    /// </summary>
    /// <param name="normalizedType">The type, as <see cref="NormalizeType"/> spells it.</param>
    /// <returns>The settings, as strings.</returns>
    internal static IReadOnlyDictionary<string, string> SettingsFor(string normalizedType)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = normalizedType
        };
}

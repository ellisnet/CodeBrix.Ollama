using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What converting a stored model to GGUF knows that is not about the store: which sources it will take, the
/// tag the result is stored under, the Modelfile that creates it, and the settings its provenance records.
/// </summary>
/// <remarks>
/// Everything here works on what a model's layers already say, so a source that cannot be converted is refused
/// before a single byte has been laid out on disk - which for a checkpoint is the difference between a message
/// in a moment and a message after several gigabytes have been linked into a temporary folder.
/// </remarks>
internal static class GgufConvert
{
    /// <summary>The tag a converted model is stored under when the caller names none and takes the default type.</summary>
    internal const string GgufTag = "gguf";

    /// <summary>The configuration file whose <c>architectures</c> decide whether a checkpoint can be read.</summary>
    internal const string ConfigFileName = "config.json";

    /// <summary>The tokenizer configuration a synthesized Modelfile takes its stop parameter from.</summary>
    internal const string TokenizerConfigFileName = "tokenizer_config.json";

    /// <summary>The name the GGUF file is written under inside the working folder.</summary>
    internal const string OutputFileName = "model.gguf";

    /// <summary>The status reported before the converted file is created as a model.</summary>
    internal const string CreatingStatus = "creating model";

    /// <summary>
    /// The tag the converted model takes: <c>gguf</c> on its own when the type was left to the checkpoint, and
    /// the type appended when the caller forced one, so that a name says what is inside the file.
    /// </summary>
    /// <param name="outputType">The type the caller asked for.</param>
    /// <returns>The tag.</returns>
    internal static string TagFor(GgufOutputType outputType)
    {
        switch (outputType)
        {
            case GgufOutputType.F32:
                return GgufTag + "-f32";
            case GgufOutputType.F16:
                return GgufTag + "-f16";
            case GgufOutputType.BF16:
                return GgufTag + "-bf16";
            default:
                return GgufTag;
        }
    }

    /// <summary>
    /// Refuses a source that is not a transformers checkpoint, naming what it holds instead.
    /// </summary>
    /// <param name="files">The source model's files.</param>
    /// <param name="sourceName">The source model, spelled as it is stored.</param>
    /// <exception cref="InvalidOperationException">The bundle already holds GGUF or ONNX files.</exception>
    /// <exception cref="CheckpointFormatException">The bundle holds no <c>config.json</c>.</exception>
    internal static void RequireCheckpoint(IReadOnlyList<ResolvedFile> files, string sourceName)
    {
        string gguf = FirstWithExtension(files, ".gguf");
        if (gguf != null)
        {
            throw new InvalidOperationException(
                "The model " + sourceName + " already holds a GGUF file (" + gguf + "), so there is nothing"
                    + " to convert. A GGUF model is loaded through ResolveAsync as it stands.");
        }

        string onnx = FirstWithExtension(files, ".onnx");
        if (onnx != null)
        {
            throw new InvalidOperationException(
                "The model " + sourceName + " holds an exported ONNX graph (" + onnx + ") rather than a"
                    + " transformers checkpoint. Converting to GGUF reads a checkpoint - the weights, the"
                    + " configuration and the tokenizer files a publisher ships - not a graph.");
        }

        if (!Holds(files, ConfigFileName))
        {
            throw new CheckpointFormatException(
                "The model " + sourceName + " holds no " + ConfigFileName + ", so it is not a checkpoint this"
                    + " library can convert.");
        }
    }

    /// <summary>
    /// Reads the source's <c>config.json</c> the way the conversion will read it, so that an architecture this
    /// version does not convert is named before anything is laid out on disk.
    /// </summary>
    /// <param name="configJson">The text of the source's <c>config.json</c>.</param>
    /// <param name="requested">The architecture the caller asked for.</param>
    /// <param name="sourceName">The source model, spelled as it is stored, for the message.</param>
    /// <exception cref="NotSupportedException">The configuration names an architecture this version cannot read.</exception>
    /// <exception cref="CheckpointFormatException">The configuration cannot be read.</exception>
    internal static void RequireSupportedArchitecture(string configJson, CheckpointArchitecture requested,
        string sourceName)
    {
        HuggingFaceConfig config = HuggingFaceConfig.Parse(
            Encoding.UTF8.GetBytes(configJson ?? string.Empty), sourceName + "/" + ConfigFileName);
        GgufConversion.RequireSupportedArchitecture(config, requested);
    }

    /// <summary>
    /// Builds the Modelfile that turns the converted file into a model: where the weights are, what the
    /// tokenizer configuration says generation stops on, and the licence text the source ships when it ships one.
    /// </summary>
    /// <param name="ggufPath">The absolute path of the converted file.</param>
    /// <param name="tokenizerConfigJson">
    /// The text of the source's <c>tokenizer_config.json</c>, or <see langword="null"/>.
    /// </param>
    /// <param name="licenseText">The source's licence text, or <see langword="null"/>.</param>
    /// <returns>The Modelfile.</returns>
    /// <remarks>
    /// NO TEMPLATE LINE IS SYNTHESIZED, and that is deliberate. A checkpoint's chat template is Jinja and is
    /// written into the GGUF file itself, where a runner reads it; a Modelfile's TEMPLATE is the other dialect
    /// altogether, the one that only a Modelfile ever carries. Copying the one into the other would hand a
    /// consumer a template that the engine it names cannot render.
    /// </remarks>
    internal static Modelfile BuildModelfile(string ggufPath, string tokenizerConfigJson, string licenseText)
    {
        var commands = new List<ModelfileCommand> { new ModelfileCommand("model", ggufPath) };

        string stop = ReadStopToken(tokenizerConfigJson);
        if (stop != null)
        {
            commands.Add(new ModelfileCommand("stop", stop));
        }

        if (!string.IsNullOrWhiteSpace(licenseText))
        {
            commands.Add(new ModelfileCommand("license", licenseText));
        }

        return new Modelfile(commands);
    }

    /// <summary>
    /// The text a conversion writes as the model's one stop parameter: the end-of-sequence token the
    /// tokenizer configuration names, when it names one.
    /// </summary>
    /// <param name="tokenizerConfigJson">The text of <c>tokenizer_config.json</c>, or <see langword="null"/>.</param>
    /// <returns>The token text, or <see langword="null"/> when the file names none.</returns>
    internal static string ReadStopToken(string tokenizerConfigJson)
    {
        if (string.IsNullOrWhiteSpace(tokenizerConfigJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(tokenizerConfigJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("eos_token", out JsonElement value))
            {
                return null;
            }

            string content = value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ValueKind == JsonValueKind.Object
                    && value.TryGetProperty("content", out JsonElement inner)
                    && inner.ValueKind == JsonValueKind.String
                        ? inner.GetString()
                        : null;

            return string.IsNullOrEmpty(content) ? null : content;
        }
        catch (JsonException)
        {
            //A tokenizer configuration that is not JSON decides nothing here; the conversion itself refuses it.
            return null;
        }
    }

    /// <summary>
    /// The settings a converted model records about the conversion that produced it.
    /// </summary>
    /// <param name="options">The options the caller gave.</param>
    /// <param name="modelId">The identifier the general metadata was derived from.</param>
    /// <param name="result">What the conversion reported.</param>
    /// <returns>The settings, as strings.</returns>
    internal static IReadOnlyDictionary<string, string> SettingsFor(ConvertOptions options, string modelId,
        ConvertResult result)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["outputType"] = result.TypeWritten.ToString(),
            ["requestedOutputType"] = options.OutputType.ToString(),
            ["architecture"] = result.Architecture.ToString(),
            ["requestedArchitecture"] = options.Architecture.ToString(),
            ["modelId"] = modelId,
            ["addedSpecialTokens"] = JoinTokens(options.AddedSpecialTokens)
        };

    /// <summary>
    /// Renders the token contents the caller supplied for the provenance record.
    /// </summary>
    /// <param name="tokens">The contents, or <see langword="null"/>.</param>
    /// <returns>The contents joined by commas, or an empty string when none were supplied.</returns>
    private static string JoinTokens(IReadOnlyList<string> tokens)
    {
        if (tokens == null || tokens.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i > 0)
            {
                text.Append(',');
            }
            text.Append(tokens[i]);
        }

        return text.ToString();
    }

    /// <summary>
    /// The first of a model's files whose name ends with an extension, or <see langword="null"/>.
    /// </summary>
    /// <param name="files">The model's files.</param>
    /// <param name="extension">The extension, with its dot.</param>
    /// <returns>The publisher's path of the first such file.</returns>
    private static string FirstWithExtension(IReadOnlyList<ResolvedFile> files, string extension)
    {
        foreach (ResolvedFile file in files)
        {
            if (file.Name != null && file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return file.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a model holds a file at the top of its tree under this exact name.
    /// </summary>
    /// <param name="files">The model's files.</param>
    /// <param name="path">The publisher's path to look for.</param>
    /// <returns><see langword="true"/> when the model holds it.</returns>
    private static bool Holds(IReadOnlyList<ResolvedFile> files, string path)
    {
        foreach (ResolvedFile file in files)
        {
            if (string.Equals(file.Name, path, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

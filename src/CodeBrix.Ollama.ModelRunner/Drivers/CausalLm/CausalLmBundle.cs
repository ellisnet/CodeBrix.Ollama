using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a bundle's generation configuration says about itself: whether it holds a single-graph causal
/// language model at all, which graph that is, how its tensors are named, and which token numbers begin and
/// end a sequence.
/// </summary>
/// <remarks>
/// <para>
/// THE DRIVER IS CHOSEN FROM THE BUNDLE, NOT FROM THE CALLER. A bundle that carries a
/// <c>genai_config.json</c> with a decoder block is driven by this driver; one that describes an
/// encoder-decoder, a vision or speech model, or a pipeline of several graphs is refused by name, because
/// each of those is a different loop and not a different setting.
/// </para>
/// <para>
/// NOTHING ABOUT ANY PARTICULAR MODEL IS WRITTEN DOWN HERE. The layer count, the head counts, the head size,
/// the context length, the vocabulary size and every tensor name are read out of the file; what is checked is
/// that they are present and make sense together.
/// </para>
/// </remarks>
internal sealed class CausalLmBundle
{
    /// <summary>The file a bundle of this kind describes its generation in.</summary>
    internal const string ConfigurationFileName = "genai_config.json";

    private CausalLmBundle(
        IReadOnlyDictionary<string, string> files,
        CausalLmDecoder decoder,
        string architecture,
        int vocabularySize,
        int contextLength,
        int beginningOfSequence,
        IReadOnlyList<int> endOfSequence,
        int padding)
    {
        Files = files;
        Decoder = decoder;
        Architecture = architecture;
        VocabularySize = vocabularySize;
        ContextLength = contextLength;
        BeginningOfSequence = beginningOfSequence;
        EndOfSequence = endOfSequence;
        Padding = padding;
    }

    /// <summary>The bundle's files: the publisher's name for each one against the path it is really at.</summary>
    internal IReadOnlyDictionary<string, string> Files { get; }

    /// <summary>What the decoder block said.</summary>
    internal CausalLmDecoder Decoder { get; }

    /// <summary>The model type the bundle names, kept for messages and for what it reports about itself.</summary>
    internal string Architecture { get; }

    /// <summary>How many token numbers the model answers over.</summary>
    internal int VocabularySize { get; }

    /// <summary>The longest sequence the model was trained for, which is what fills its context.</summary>
    internal int ContextLength { get; }

    /// <summary>The beginning-of-sequence token number, or -1 when the bundle names none.</summary>
    internal int BeginningOfSequence { get; }

    /// <summary>The end-of-sequence token numbers, of which a bundle may name more than one.</summary>
    internal IReadOnlyList<int> EndOfSequence { get; }

    /// <summary>The padding token number, or -1 when the bundle names none.</summary>
    internal int Padding { get; }

    /// <summary>Reads a bundle laid out as a directory.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns>What the bundle says about itself, with file names relative to the directory.</returns>
    /// <exception cref="ModelLoadException">
    /// There is no such directory, it holds no <c>genai_config.json</c>, or the configuration describes
    /// something this driver does not generate for.
    /// </exception>
    internal static CausalLmBundle FromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new ModelLoadException("There is no bundle directory at '" + directory + "'.");
        }

        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            files[Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/')] = path;
        }

        if (!TryConfiguration(files, out string configuration))
        {
            throw new ModelLoadException(
                "The bundle at '" + directory + "' has no " + ConfigurationFileName + ", so there is nothing"
                + " to say how its graph is driven.");
        }

        return Read(files, configuration, "the bundle at '" + directory + "'");
    }

    /// <summary>
    /// Reads a bundle held as a set of (logical file name to path) pairs, which is what a content-addressed
    /// store gives - its files are kept under digests and the pairs say which is which.
    /// </summary>
    /// <param name="files">The bundle's files.</param>
    /// <returns>What the bundle says about itself.</returns>
    /// <exception cref="ModelLoadException">
    /// There is no <c>genai_config.json</c> among them, or the configuration describes something this driver
    /// does not generate for.
    /// </exception>
    internal static CausalLmBundle FromFiles(IReadOnlyDictionary<string, string> files)
    {
        Dictionary<string, string> copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> file in files) copy[file.Key] = file.Value;

        if (!TryConfiguration(copy, out string configuration))
        {
            throw new ModelLoadException(
                "None of the files given is a " + ConfigurationFileName + ", so there is nothing to say how"
                + " their graph is driven.");
        }

        return Read(copy, configuration, "the files given");
    }

    /// <summary>Whether a bundle laid out as a directory is one this driver generates for.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns><see langword="true"/> when it holds a generation configuration.</returns>
    internal static bool IsCausalLmDirectory(string directory) =>
        Directory.Exists(directory) && File.Exists(Path.Combine(directory, ConfigurationFileName));

    /// <summary>The path one of the bundle's files is at, or <see langword="null"/> when it holds no such file.</summary>
    /// <param name="name">The file's name inside the bundle.</param>
    /// <returns>The path.</returns>
    internal string FindFile(string name)
    {
        foreach (KeyValuePair<string, string> file in Files)
        {
            if (LeafName(file.Key) == name) return file.Value;
        }

        return null;
    }

    private static bool TryConfiguration(IReadOnlyDictionary<string, string> files, out string path)
    {
        foreach (KeyValuePair<string, string> file in files)
        {
            if (LeafName(file.Key) != ConfigurationFileName) continue;

            path = file.Value;
            return true;
        }

        path = null;
        return false;
    }

    private static CausalLmBundle Read(
        IReadOnlyDictionary<string, string> files, string configuration, string where)
    {
        if (!File.Exists(configuration))
        {
            throw new ModelLoadException("There is no file at '" + configuration + "'.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(configuration));
        }
        catch (JsonException exception)
        {
            throw new ModelLoadException(
                "The " + ConfigurationFileName + " of " + where + " is not valid JSON.", exception);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("model", out JsonElement model)
                || model.ValueKind != JsonValueKind.Object)
            {
                throw new ModelLoadException(
                    "The " + ConfigurationFileName + " of " + where + " has no model block, so there is"
                    + " nothing to say what it holds.");
            }

            RefuseWhatThisDriverDoesNotRun(model, where);

            if (!model.TryGetProperty("decoder", out JsonElement decoder)
                || decoder.ValueKind != JsonValueKind.Object)
            {
                throw new ModelLoadException(
                    "The " + ConfigurationFileName + " of " + where + " has no decoder block, and this driver"
                    + " generates text one token at a time from a decoder.");
            }

            if (decoder.TryGetProperty("pipeline", out JsonElement pipeline)
                && pipeline.ValueKind != JsonValueKind.Null)
            {
                throw new ModelLoadException(
                    "The " + ConfigurationFileName + " of " + where + " describes a decoder PIPELINE of"
                    + " several graphs run in turn, and this driver runs one graph.");
            }

            return new CausalLmBundle(
                files,
                ReadDecoder(decoder, where),
                Text(model, "type", "(none stated)"),
                Number(model, "vocab_size", where),
                Number(model, "context_length", where),
                OptionalNumber(model, "bos_token_id"),
                EndOfSequenceNumbers(model),
                OptionalNumber(model, "pad_token_id"));
        }
    }

    private static void RefuseWhatThisDriverDoesNotRun(JsonElement model, string where)
    {
        //Each of these names a DIFFERENT generation loop - one that runs an encoder first, or that takes
        //pixels or audio - rather than a setting of this one, so each is refused by the name the file gives
        //it instead of being ignored into a wrong answer.
        string[] blocks = { "encoder", "encoder_decoder_init", "vision", "speech", "audio", "embedding" };
        foreach (string block in blocks)
        {
            if (!model.TryGetProperty(block, out JsonElement element)
                || element.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            throw new ModelLoadException(
                "The " + ConfigurationFileName + " of " + where + " has a '" + block + "' block, so it is not"
                + " a single-graph causal language model, and this driver generates text from one of those.");
        }
    }

    private static CausalLmDecoder ReadDecoder(JsonElement decoder, string where)
    {
        if (!decoder.TryGetProperty("inputs", out JsonElement inputs)
            || inputs.ValueKind != JsonValueKind.Object)
        {
            throw new ModelLoadException(
                "The decoder block of " + where + " does not say what its graph's inputs are called.");
        }

        if (!decoder.TryGetProperty("outputs", out JsonElement outputs)
            || outputs.ValueKind != JsonValueKind.Object)
        {
            throw new ModelLoadException(
                "The decoder block of " + where + " does not say what its graph's outputs are called.");
        }

        int layers = Number(decoder, "num_hidden_layers", where);
        int heads = Number(decoder, "num_attention_heads", where);
        int keyValueHeads = OptionalNumber(decoder, "num_key_value_heads");
        if (keyValueHeads < 0) keyValueHeads = heads;

        int headSize = OptionalNumber(decoder, "head_size");
        int hidden = OptionalNumber(decoder, "hidden_size");
        if (headSize < 0 && hidden > 0 && heads > 0) headSize = hidden / heads;

        if (layers < 1 || heads < 1 || keyValueHeads < 1 || headSize < 1)
        {
            throw new ModelLoadException(
                "The decoder block of " + where + " states " + layers.ToString(CultureInfo.InvariantCulture)
                + " layers, " + heads.ToString(CultureInfo.InvariantCulture) + " heads, "
                + keyValueHeads.ToString(CultureInfo.InvariantCulture) + " key and value heads and a head size"
                + " of " + headSize.ToString(CultureInfo.InvariantCulture)
                + ", and a cache cannot be built from that.");
        }

        if (keyValueHeads > heads)
        {
            throw new ModelLoadException(
                "The decoder block of " + where + " states more key and value heads ("
                + keyValueHeads.ToString(CultureInfo.InvariantCulture) + ") than attention heads ("
                + heads.ToString(CultureInfo.InvariantCulture) + ").");
        }

        return new CausalLmDecoder(
            Text(decoder, "filename", null)
                ?? throw new ModelLoadException(
                    "The decoder block of " + where + " does not name its graph file."),
            Text(inputs, "input_ids", null)
                ?? throw new ModelLoadException(
                    "The decoder block of " + where + " does not say what its token-number input is called."),
            Text(inputs, "attention_mask", null),
            Text(inputs, "position_ids", null),
            Pattern(inputs, "past_key_names", where),
            Pattern(inputs, "past_value_names", where),
            Text(outputs, "logits", null)
                ?? throw new ModelLoadException(
                    "The decoder block of " + where + " does not say what its answer output is called."),
            Pattern(outputs, "present_key_names", where),
            Pattern(outputs, "present_value_names", where),
            layers,
            heads,
            keyValueHeads,
            headSize,
            hidden);
    }

    private static string Pattern(JsonElement element, string name, string where)
    {
        string value = Text(element, name, null);
        if (value == null)
        {
            throw new ModelLoadException(
                "The decoder block of " + where + " does not state '" + name + "', so its per-layer cache"
                + " tensors cannot be named.");
        }

        if (!value.Contains("%d", StringComparison.Ordinal))
        {
            throw new ModelLoadException(
                "The decoder block of " + where + " states '" + name + "' as '" + value + "', which has no"
                + " '%d' for the layer number in it.");
        }

        return value;
    }

    private static IReadOnlyList<int> EndOfSequenceNumbers(JsonElement model)
    {
        List<int> numbers = new List<int>();
        if (!model.TryGetProperty("eos_token_id", out JsonElement element)) return numbers;

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement one in element.EnumerateArray())
            {
                if (one.TryGetInt32(out int number)) numbers.Add(number);
            }

            return numbers;
        }

        if (element.TryGetInt32(out int single)) numbers.Add(single);
        return numbers;
    }

    private static string Text(JsonElement element, string name, string fallback)
    {
        if (element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString();
            if (!string.IsNullOrEmpty(text)) return text;
        }

        return fallback;
    }

    private static int Number(JsonElement element, string name, string where)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number))
        {
            return number;
        }

        throw new ModelLoadException(
            "The " + ConfigurationFileName + " of " + where + " does not state '" + name
            + "' as a whole number.");
    }

    private static int OptionalNumber(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : -1;

    private static string LeafName(string name)
    {
        int slash = name.LastIndexOfAny(new[] { '/', '\\' });
        return slash < 0 ? name : name.Substring(slash + 1);
    }
}

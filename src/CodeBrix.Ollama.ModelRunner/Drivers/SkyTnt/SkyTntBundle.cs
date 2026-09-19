using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a bundle says about itself: whether it holds a MIDI model of this family at all, which tokenizer it
/// wants, and where its two graphs are.
/// </summary>
/// <remarks>
/// <para>
/// THE DRIVER IS CHOSEN FROM THE BUNDLE, NOT FROM THE CALLER. A bundle whose <c>config.json</c> names this
/// family's architecture is driven by this driver, and one that does not is refused with a message naming
/// what was found instead - which is what keeps the engine itself free of any knowledge of one publisher's
/// model.
/// </para>
/// <para>
/// THE TWO GRAPHS ARE FOUND BY NAME AND CONFIRMED BY SHAPE. The publisher's file names are looked for first,
/// then any graph whose name says which of the two it is; whichever way one is found, it is only accepted
/// once the loaded graph has been seen to declare the inputs and outputs the loop needs, so a mis-named file
/// fails at load with a sentence rather than at the first step with nonsense.
/// </para>
/// </remarks>
internal sealed class SkyTntBundle
{
    /// <summary>The file a bundle describes itself in.</summary>
    internal const string ConfigurationFileName = "config.json";

    /// <summary>The publisher's name for the graph that turns events into hidden states.</summary>
    internal const string BaseGraphFileName = "model_base.onnx";

    /// <summary>The publisher's name for the graph that turns a hidden state into an event's tokens.</summary>
    internal const string TokenGraphFileName = "model_token.onnx";

    private SkyTntBundle(
        SkyTntTokenizerConfiguration tokenizer, string baseGraph, string tokenGraph, string architecture)
    {
        Tokenizer = tokenizer;
        BaseGraph = baseGraph;
        TokenGraph = tokenGraph;
        Architecture = architecture;
    }

    /// <summary>What the bundle says about its tokenizer.</summary>
    internal SkyTntTokenizerConfiguration Tokenizer { get; }

    /// <summary>The name of the graph that turns events into hidden states, as the bundle holds it.</summary>
    internal string BaseGraph { get; }

    /// <summary>The name of the graph that turns a hidden state into an event's tokens.</summary>
    internal string TokenGraph { get; }

    /// <summary>The architecture the bundle names, kept for messages.</summary>
    internal string Architecture { get; }

    /// <summary>
    /// Reads a bundle laid out as a directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>What the bundle says about itself, with graph names relative to the directory.</returns>
    /// <exception cref="ModelLoadException">
    /// There is no such directory, it has no <c>config.json</c>, the configuration is not a MIDI model of this
    /// family, or one of the two graphs is not there.
    /// </exception>
    internal static SkyTntBundle FromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new ModelLoadException("There is no bundle directory at '" + directory + "'.");
        }

        string configuration = Path.Combine(directory, ConfigurationFileName);
        if (!File.Exists(configuration))
        {
            throw new ModelLoadException(
                "The bundle at '" + directory + "' has no " + ConfigurationFileName + ", so there is nothing"
                + " to say which model it holds.");
        }

        List<string> names = new List<string>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.onnx", SearchOption.AllDirectories))
        {
            names.Add(Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'));
        }

        return Read(File.ReadAllBytes(configuration), names, "the bundle at '" + directory + "'");
    }

    /// <summary>
    /// Reads a bundle held as a set of (logical file name to path) pairs, which is what a content-addressed
    /// store gives - its files are kept under digests and the pairs say which is which.
    /// </summary>
    /// <param name="files">The bundle's files.</param>
    /// <returns>What the bundle says about itself, with graph names as the bundle's own.</returns>
    /// <exception cref="ModelLoadException">
    /// There is no <c>config.json</c> among them, the configuration is not a MIDI model of this family, or one
    /// of the two graphs is not there.
    /// </exception>
    internal static SkyTntBundle FromFiles(IReadOnlyDictionary<string, string> files)
    {
        string configuration = null;
        List<string> names = new List<string>();
        foreach (KeyValuePair<string, string> file in files)
        {
            if (LeafName(file.Key) == ConfigurationFileName) configuration = file.Value;
            if (file.Key.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) names.Add(file.Key);
        }

        if (configuration == null)
        {
            throw new ModelLoadException(
                "None of the files given is a " + ConfigurationFileName + ", so there is nothing to say which"
                + " model they hold.");
        }

        if (!File.Exists(configuration))
        {
            throw new ModelLoadException("There is no file at '" + configuration + "'.");
        }

        return Read(File.ReadAllBytes(configuration), names, "the files given");
    }

    private static SkyTntBundle Read(byte[] configuration, List<string> graphs, string where)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(configuration);
        }
        catch (JsonException exception)
        {
            throw new ModelLoadException(
                "The " + ConfigurationFileName + " of " + where + " is not valid JSON.", exception);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            string architecture = Architectures(root);
            if (!IsMidiModel(root, architecture))
            {
                throw new ModelLoadException(
                    "The " + ConfigurationFileName + " of " + where + " names the architecture '"
                    + architecture + "', and this driver generates MIDI for a model that names 'MIDIModel'.");
            }

            if (!root.TryGetProperty("tokenizer", out JsonElement tokenizer)
                || tokenizer.ValueKind != JsonValueKind.Object)
            {
                throw new ModelLoadException(
                    "The " + ConfigurationFileName + " of " + where + " has no tokenizer block, so there is"
                    + " nothing to say what its tokens mean.");
            }

            return new SkyTntBundle(
                ReadTokenizer(tokenizer, where),
                Graph(graphs, BaseGraphFileName, "base", where),
                Graph(graphs, TokenGraphFileName, "token", where),
                architecture);
        }
    }

    private static bool IsMidiModel(JsonElement root, string architecture)
    {
        if (string.Equals(architecture, "MIDIModel", StringComparison.Ordinal)) return true;

        return root.TryGetProperty("model_type", out JsonElement modelType)
            && modelType.ValueKind == JsonValueKind.String
            && string.Equals(modelType.GetString(), "midi_model", StringComparison.Ordinal);
    }

    private static string Architectures(JsonElement root)
    {
        if (root.TryGetProperty("architectures", out JsonElement architectures)
            && architectures.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement one in architectures.EnumerateArray())
            {
                if (one.ValueKind == JsonValueKind.String) return one.GetString();
            }
        }

        if (root.TryGetProperty("model_type", out JsonElement modelType)
            && modelType.ValueKind == JsonValueKind.String)
        {
            return modelType.GetString();
        }

        return "(none stated)";
    }

    private static SkyTntTokenizerConfiguration ReadTokenizer(JsonElement tokenizer, string where)
    {
        Dictionary<string, IReadOnlyList<string>> events =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (tokenizer.TryGetProperty("events", out JsonElement declared)
            && declared.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty one in declared.EnumerateObject())
            {
                List<string> parameters = new List<string>();
                if (one.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement parameter in one.Value.EnumerateArray())
                    {
                        if (parameter.ValueKind == JsonValueKind.String) parameters.Add(parameter.GetString());
                    }
                }

                events[one.Name] = parameters;
            }
        }

        Dictionary<string, int> sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        if (tokenizer.TryGetProperty("event_parameters", out JsonElement parameterSizes)
            && parameterSizes.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty one in parameterSizes.EnumerateObject())
            {
                if (one.Value.TryGetInt32(out int size)) sizes[one.Name] = size;
            }
        }

        return new SkyTntTokenizerConfiguration(
            Text(tokenizer, "version", where),
            Flag(tokenizer, "optimise_midi"),
            Number(tokenizer, "vocab_size", where),
            Number(tokenizer, "max_token_seq", where),
            Number(tokenizer, "pad_id", where),
            Number(tokenizer, "bos_id", where),
            Number(tokenizer, "eos_id", where),
            events,
            sizes);
    }

    private static string Text(JsonElement element, string name, string where)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        throw new ModelLoadException(
            "The tokenizer block of " + where + " does not state '" + name + "'.");
    }

    private static int Number(JsonElement element, string name, string where)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number))
        {
            return number;
        }

        throw new ModelLoadException(
            "The tokenizer block of " + where + " does not state '" + name + "' as a whole number.");
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string Graph(List<string> graphs, string fileName, string which, string where)
    {
        foreach (string name in graphs)
        {
            if (LeafName(name) == fileName) return name;
        }

        //A bundle that renamed the file is still usable as long as its name says which of the two it is.
        string marker = which == "base" ? "base" : "token";
        foreach (string name in graphs)
        {
            if (LeafName(name).Contains(marker, StringComparison.OrdinalIgnoreCase)) return name;
        }

        throw new ModelLoadException(
            "There is no " + which + " graph in " + where + ": nothing is called '" + fileName + "' and no"
            + " other graph names itself as the " + which + " one.");
    }

    private static string LeafName(string name)
    {
        int slash = name.LastIndexOfAny(new[] { '/', '\\' });
        return slash < 0 ? name : name.Substring(slash + 1);
    }
}

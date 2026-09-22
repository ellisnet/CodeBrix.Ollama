using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>The portable bundle contract, independent of any model store or staging implementation.</summary>
internal sealed class MuseCocoBundle
{
    private readonly IReadOnlyDictionary<string, string> _files;
    internal JsonElement Metadata { get; private set; }
    internal MusicAttributeSchema Schema { get; private set; }

    private MuseCocoBundle(IReadOnlyDictionary<string, string> files) => _files = files;

    internal static Task<MuseCocoBundle> FromDirectoryAsync(string directory, string kind, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A bundle directory is required.", nameof(directory));
        string root = Path.GetFullPath(directory);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'), file);
        }
        return FromFilesAsync(files, kind, cancellationToken);
    }

    internal static async Task<MuseCocoBundle> FromFilesAsync(
        IReadOnlyDictionary<string, string> files, string kind, CancellationToken cancellationToken)
    {
        if (files == null) throw new ArgumentNullException(nameof(files));
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in files)
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) throw new ArgumentException("Empty bundle file path.", nameof(files));
            copy.Add(ValidateName(pair.Key), Path.GetFullPath(pair.Value));
        }
        var bundle = new MuseCocoBundle(copy);
        try
        {
            bundle.Metadata = await bundle.ReadJsonAsync("musecoco.json", cancellationToken).ConfigureAwait(false);
            if (String(bundle.Metadata, "format") != "codebrix.musecoco.v1" || String(bundle.Metadata, "kind") != kind)
            {
                throw new ModelLoadException("The bundle is not a supported MuseCoco " + kind + " model.");
            }
            JsonElement schema = await bundle.ReadJsonAsync(String(bundle.Metadata, "schema"), cancellationToken).ConfigureAwait(false);
            bundle.Schema = MusicAttributeSchema.Parse(schema);
            bundle.Resolve(String(bundle.Metadata, "graph"));
            return bundle;
        }
        catch (Exception error) when (error is JsonException || error is KeyNotFoundException
            || error is InvalidOperationException || error is FormatException || error is OverflowException)
        {
            throw new ModelLoadException("The MuseCoco bundle metadata is invalid: " + error.Message, error);
        }
    }

    internal async Task<JsonElement> ReadJsonAsync(string name, CancellationToken cancellationToken)
    {
        string path = Resolve(name);
        try
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
            return document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw new ModelLoadException("Invalid MuseCoco JSON file '" + name + "'.", error);
        }
    }

    private string Resolve(string name)
    {
        name = ValidateName(name);
        if (!_files.TryGetValue(name, out string path)) throw new ModelLoadException("The MuseCoco bundle is missing '" + name + "'.");
        return path;
    }

    internal Task<IOnnxModel> LoadGraphAsync(OnnxRunnerOptions options, CancellationToken cancellationToken)
    {
        string graph = String(Metadata, "graph");
        return OnnxModel.LoadFromFilesAsync(_files, graph, options, cancellationToken);
    }

    internal static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':')
            || name.Split('/').Any(p => p.Length == 0 || p == "." || p == ".."))
        {
            throw new ModelLoadException("Invalid relative bundle filename: " + name);
        }
        return name;
    }

    internal static string String(JsonElement root, string key)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out JsonElement property)
            || property.ValueKind != JsonValueKind.String) throw new ModelLoadException("Missing or invalid bundle value: " + key);
        string value = property.GetString();
        if (string.IsNullOrWhiteSpace(value)) throw new ModelLoadException("Missing bundle value: " + key);
        return value;
    }

    internal static int Positive(JsonElement root, string key, int maximum)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out JsonElement property)
            || property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out int value))
            throw new ModelLoadException("Missing or invalid bundle dimension: " + key);
        if (value < 1 || value > maximum) throw new ModelLoadException("Invalid bundle dimension: " + key);
        return value;
    }

    internal static void RequireTensor(IReadOnlyList<OnnxValueMetadata> values, string name, OnnxElementType type, params long[] dimensions)
    {
        OnnxValueMetadata value = values.SingleOrDefault(v => v.Name == name);
        if (value == null || value.ElementType != type || value.Shape.Count != dimensions.Length)
        {
            throw new ModelLoadException("The MuseCoco graph has an invalid tensor contract: " + name);
        }
        for (int i = 0; i < dimensions.Length; i++)
        {
            if (dimensions[i] >= 0 && value.Shape[i].Length != dimensions[i])
            {
                throw new ModelLoadException("The MuseCoco graph has an invalid dimension for: " + name);
            }
        }
    }
}

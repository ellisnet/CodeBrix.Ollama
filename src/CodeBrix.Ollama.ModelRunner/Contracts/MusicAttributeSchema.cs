using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>The immutable attribute contract shared by a music model and its optional text classifier.</summary>
public sealed class MusicAttributeSchema
{
    private readonly Dictionary<string, int> _indices;
    private readonly string _signature;

    private MusicAttributeSchema(string id, MusicAttributeDefinition[] definitions)
    {
        Id = id;
        Definitions = Array.AsReadOnly(definitions);
        _indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < definitions.Length; i++)
        {
            if (!_indices.TryAdd(definitions[i].Name, i))
            {
                throw new ModelLoadException("Duplicate music attribute: " + definitions[i].Name);
            }
        }
        _signature = JsonSerializer.Serialize(new
        {
            id,
            definitions = definitions.Select(d => new { d.Name, d.Key, d.Values, d.Tokens, d.DefaultIndex, d.Classifier })
        });
    }

    /// <summary>The bundle's versioned attribute-schema identifier.</summary>
    public string Id { get; }

    /// <summary>The definitions in the order expected by the music model's attribute prefix.</summary>
    public IReadOnlyList<MusicAttributeDefinition> Definitions { get; }

    /// <summary>Creates a value set with every attribute at its unspecified default.</summary>
    public MusicAttributes CreateAttributes() => new MusicAttributes(this, Definitions.Select(d => d.DefaultIndex).ToArray());

    /// <summary>Whether two bundles use exactly the same names, categories, prefix and classifier mappings.</summary>
    public bool IsCompatibleWith(MusicAttributeSchema other) => other != null && _signature == other._signature;

    internal int IndexOf(string name)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        if (!_indices.TryGetValue(name, out int index))
        {
            throw new ArgumentException("Unknown music attribute: " + name, nameof(name));
        }
        return index;
    }

    internal static MusicAttributeSchema Parse(JsonElement root)
    {
        if (MuseCocoBundle.String(root, "format") != "codebrix.music-attributes.v1")
        {
            throw new ModelLoadException("Unsupported music attribute schema format.");
        }
        var definitions = new List<MusicAttributeDefinition>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var classifiers = new HashSet<int>();
        foreach (JsonElement entry in root.GetProperty("definitions").EnumerateArray())
        {
            string name = MuseCocoBundle.String(entry, "name");
            string key = MuseCocoBundle.String(entry, "key");
            string[] values = entry.GetProperty("values").EnumerateArray().Select(v => v.GetString()).ToArray();
            string[] tokens = entry.GetProperty("tokens").EnumerateArray().Select(v => v.GetString()).ToArray();
            int defaultIndex = entry.GetProperty("default").GetInt32();
            int classifier = entry.GetProperty("bertHead").GetInt32();
            if (values.Length < 2 || values.Length > 256 || tokens.Length != values.Length
                || defaultIndex < 0 || defaultIndex >= values.Length || classifier < -1
                || values.Any(string.IsNullOrWhiteSpace) || tokens.Any(string.IsNullOrWhiteSpace)
                || values.Distinct(StringComparer.Ordinal).Count() != values.Length
                || tokens.Distinct(StringComparer.Ordinal).Count() != tokens.Length
                || !keys.Add(key) || (classifier >= 0 && !classifiers.Add(classifier)))
            {
                throw new ModelLoadException("Invalid music attribute definition: " + name);
            }
            definitions.Add(new MusicAttributeDefinition(name, key, values, tokens, defaultIndex, classifier));
        }
        if (definitions.Count == 0 || definitions.Count > 128
            || classifiers.Any(i => i >= classifiers.Count))
        {
            throw new ModelLoadException("Invalid music attribute count or classifier ordering.");
        }
        return new MusicAttributeSchema(MuseCocoBundle.String(root, "id"), definitions.ToArray());
    }
}

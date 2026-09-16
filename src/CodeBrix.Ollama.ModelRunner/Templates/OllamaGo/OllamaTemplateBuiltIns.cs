using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama template/template.go;

/// <summary>
/// Ollama's twenty built-in chat templates, their stop-parameter sidecars and the index that maps an
/// arbitrary chat template to one of them. All forty-one files ship inside this assembly as embedded
/// resources, exactly as Ollama embeds them in its binary.
/// </summary>
internal static class OllamaTemplateBuiltIns
{
    private const string ResourcePrefix = "CodeBrix.Ollama.ModelRunner.Templates.OllamaGo.BuiltIn.";

    private static readonly object Gate = new object();
    private static List<OllamaNamedTemplate> _index;
    private static List<string> _names;
    private static readonly Dictionary<string, string> Texts = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The names of the built-in templates, in alphabetical order.</summary>
    internal static IReadOnlyList<string> Names
    {
        get
        {
            EnsureLoaded();
            return _names;
        }
    }

    /// <summary>The entries of <c>index.json</c>, in file order.</summary>
    internal static IReadOnlyList<OllamaNamedTemplate> Index
    {
        get
        {
            EnsureLoaded();
            return _index;
        }
    }

    /// <summary>
    /// Reads the text of one built-in template, with its line endings normalized to newlines the way
    /// Ollama normalizes them.
    /// </summary>
    /// <param name="name">The name of the built-in, without the <c>.gotmpl</c> suffix.</param>
    /// <returns>The template text, or <see langword="null"/> when there is no such built-in.</returns>
    internal static string ReadTemplate(string name)
    {
        EnsureLoaded();
        lock (Gate)
        {
            if (Texts.TryGetValue(name, out string cached))
            {
                return cached;
            }
        }

        string text = ReadResource(name + ".gotmpl");
        if (text == null)
        {
            return null;
        }

        text = text.Replace("\r\n", "\n");
        lock (Gate)
        {
            Texts[name] = text;
        }

        return text;
    }

    /// <summary>
    /// Reads the stop parameters that accompany a built-in template.
    /// </summary>
    /// <param name="name">The name of the built-in, without the <c>.json</c> suffix.</param>
    /// <returns>The stop strings, which is empty when the built-in has no sidecar.</returns>
    internal static IReadOnlyList<string> ReadStopParameters(string name)
    {
        string json = ReadResource(name + ".json");
        List<string> stop = new List<string>();
        if (json == null)
        {
            return stop;
        }

        using (JsonDocument document = JsonDocument.Parse(json))
        {
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("stop", out JsonElement value)
                && value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        stop.Add(item.GetString());
                    }
                }
            }
        }

        return stop;
    }

    /// <summary>
    /// Lists every embedded built-in resource, which is what a test uses to prove they all load.
    /// </summary>
    /// <returns>The resource names, without the assembly prefix.</returns>
    internal static IReadOnlyList<string> ResourceFileNames()
    {
        List<string> names = new List<string>();
        foreach (string resource in typeof(OllamaTemplateBuiltIns).Assembly.GetManifestResourceNames())
        {
            if (resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                names.Add(resource.Substring(ResourcePrefix.Length));
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static string ReadResource(string fileName)
    {
        Assembly assembly = typeof(OllamaTemplateBuiltIns).Assembly;
        using (Stream stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName))
        {
            if (stream == null)
            {
                return null;
            }

            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }
    }

    private static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_index != null)
            {
                return;
            }

            List<OllamaNamedTemplate> index = new List<OllamaNamedTemplate>();
            string json = ReadResource("index.json");
            if (json != null)
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    foreach (JsonElement item in document.RootElement.EnumerateArray())
                    {
                        string name = item.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                        string template = item.TryGetProperty("template", out JsonElement t) ? t.GetString() : null;
                        index.Add(new OllamaNamedTemplate(name, template));
                    }
                }
            }

            List<string> names = new List<string>();
            foreach (string resource in typeof(OllamaTemplateBuiltIns).Assembly.GetManifestResourceNames())
            {
                if (resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                    && resource.EndsWith(".gotmpl", StringComparison.Ordinal))
                {
                    names.Add(resource.Substring(ResourcePrefix.Length,
                        resource.Length - ResourcePrefix.Length - ".gotmpl".Length));
                }
            }

            names.Sort(StringComparer.Ordinal);
            _names = names;
            _index = index;
        }
    }
}

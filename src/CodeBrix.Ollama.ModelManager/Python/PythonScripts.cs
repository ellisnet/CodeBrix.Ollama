using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The Python scripts that ship inside this package, read out of the assembly with their line endings
/// normalized. A script is a resource, never a file on disk, so nothing a consumer or a model archive
/// puts on the file system can become code this library runs.
/// </summary>
internal static class PythonScripts
{
    /// <summary>
    /// The script that reports what the embedded interpreter is: version, prefix and executable.
    /// </summary>
    internal const string ProbeVersion = "probe_version.py";

    /// <summary>
    /// The script that runs the ONNX Runtime GenAI model builder over a checkpoint folder.
    /// </summary>
    internal const string ExportGenAi = "export_genai.py";

    /// <summary>
    /// The script that runs Hugging Face Optimum's ONNX exporter over a checkpoint folder.
    /// </summary>
    internal const string ExportOptimum = "export_optimum.py";

    /// <summary>The fixed exporters for MuseCoco music and attribute BERT checkpoints.</summary>
    internal const string ExportMuseCoco = "export_musecoco.py";

    /// <summary>
    /// The script that prepares an exported graph for quantization.
    /// </summary>
    internal const string ReducePreprocess = "reduce_preprocess.py";

    /// <summary>
    /// The script that quantizes an exported graph dynamically to eight-bit weights.
    /// </summary>
    internal const string ReduceDynamic = "reduce_dynamic.py";

    /// <summary>
    /// The script that quantizes an exported graph's weights block-wise to four or eight bits.
    /// </summary>
    internal const string ReduceWeightOnly = "reduce_weight_only.py";

    /// <summary>
    /// The variable a script must leave its answer in, matching the reference implementation.
    /// </summary>
    internal const string ResultVariable = "result";

    /// <summary>The resource name prefix the scripts are embedded under.</summary>
    private const string ResourcePrefix = "CodeBrix.Ollama.ModelManager.Python.Scripts.";

    /// <summary>The cache, so a script is read out of the assembly once per process.</summary>
    private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Guards the cache.</summary>
    private static readonly object CacheLock = new object();

    /// <summary>
    /// The file names of every script that ships inside this package, in alphabetical order. It is read
    /// from the assembly rather than from a list somebody keeps up to date, so a script added without
    /// its imports being allowed for cannot slip past the check that reads them all.
    /// </summary>
    /// <returns>The script file names, such as <c>probe_version.py</c>.</returns>
    internal static IReadOnlyList<string> Names()
    {
        var names = new List<string>();
        foreach (string resource in typeof(PythonScripts).Assembly.GetManifestResourceNames())
        {
            if (resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                names.Add(resource.Substring(ResourcePrefix.Length));
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Reads one embedded script, normalizing its line endings to line feeds so that a repository
    /// checkout with carriage returns runs exactly what a checkout without them runs.
    /// </summary>
    /// <param name="scriptName">The script's file name, such as <c>probe_version.py</c>.</param>
    /// <returns>The script text.</returns>
    /// <exception cref="ArgumentException"><paramref name="scriptName"/> is null or blank.</exception>
    /// <exception cref="ModelManagerException">No script of that name ships in this package.</exception>
    internal static string Read(string scriptName)
    {
        if (string.IsNullOrWhiteSpace(scriptName))
        {
            throw new ArgumentException("A script name is required.", nameof(scriptName));
        }

        string key = scriptName.Trim();

        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out string cached))
            {
                return cached;
            }
        }

        Assembly assembly = typeof(PythonScripts).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourcePrefix + key);
        if (stream == null)
        {
            throw new ModelManagerException("the Python script '" + key + "' does not ship in this package");
        }

        using var reader = new StreamReader(stream);
        string text = NormalizeLineEndings(reader.ReadToEnd());

        lock (CacheLock)
        {
            Cache[key] = text;
        }

        return text;
    }

    /// <summary>
    /// Replaces every carriage return and carriage-return / line-feed pair with a single line feed.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The text with line-feed endings only.</returns>
    internal static string NormalizeLineEndings(string text)
        => text == null ? null : text.Replace("\r\n", "\n").Replace("\r", "\n");
}

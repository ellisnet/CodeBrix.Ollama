using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Where a graph's files are: the <c>.onnx</c> file itself, and how to find a side file it names.
/// </summary>
/// <remarks>
/// <para>
/// A model over about two gigabytes cannot put its weights in the graph file, so it names them in a side file
/// and the graph carries offsets into it. The name in the graph is the name the publisher gave the file -
/// <c>model.onnx.data</c> - which is fine when the bundle is a directory and useless when it is a content
/// store, where the same bytes sit under a digest. That is why loading takes either a directory or a set of
/// (logical name to path) pairs: the caller says what each of the publisher's names actually is, and nothing
/// has to be copied into a directory first.
/// </para>
/// <para>
/// It holds paths and nothing else. Reaching into a model store to work them out is the CALLER's business -
/// this library never names a store type.
/// </para>
/// </remarks>
internal sealed class OnnxModelLocation
{
    private readonly string _directory;
    private readonly IReadOnlyDictionary<string, string> _files;

    private OnnxModelLocation(string modelPath, string directory, IReadOnlyDictionary<string, string> files)
    {
        ModelPath = modelPath;
        _directory = directory;
        _files = files;
    }

    /// <summary>The path of the graph file itself.</summary>
    internal string ModelPath { get; }

    /// <summary>A graph file, with any side file beside it.</summary>
    /// <param name="modelPath">The graph file's path.</param>
    /// <returns>The location.</returns>
    internal static OnnxModelLocation ForFile(string modelPath)
    {
        string full = Path.GetFullPath(modelPath);
        return new OnnxModelLocation(full, Path.GetDirectoryName(full), null);
    }

    /// <summary>A graph file inside a bundle directory, with its side files in the same directory.</summary>
    /// <param name="directory">The bundle directory.</param>
    /// <param name="modelFileName">The graph file's name inside it, which may name a sub-folder.</param>
    /// <returns>The location.</returns>
    internal static OnnxModelLocation ForDirectory(string directory, string modelFileName)
    {
        string full = Path.GetFullPath(directory);
        return new OnnxModelLocation(Path.Combine(full, Normalize(modelFileName)), full, null);
    }

    /// <summary>A graph file named among a set of (logical name to path) pairs.</summary>
    /// <param name="files">The pairs, which must include the graph file and every side file it names.</param>
    /// <param name="modelFileName">The logical name of the graph file.</param>
    /// <returns>The location.</returns>
    /// <exception cref="ArgumentException">The set does not name the graph file.</exception>
    internal static OnnxModelLocation ForFiles(IReadOnlyDictionary<string, string> files, string modelFileName)
    {
        Dictionary<string, string> copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in files)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new ArgumentException("A file was named with an empty logical name.", nameof(files));
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                throw new ArgumentException(
                    "The file '" + pair.Key + "' was named with an empty path.", nameof(files));
            }

            copy[Normalize(pair.Key)] = pair.Value;
        }

        string key = Normalize(modelFileName);
        if (!copy.TryGetValue(key, out string path))
        {
            throw new ArgumentException(
                "The files do not include '" + modelFileName + "'; they name "
                + string.Join(", ", copy.Keys) + ".",
                nameof(modelFileName));
        }

        return new OnnxModelLocation(Path.GetFullPath(path), null, copy);
    }

    /// <summary>Finds a side file the graph names.</summary>
    /// <param name="location">The name as the graph writes it, with either kind of slash.</param>
    /// <returns>Its path, or <see langword="null"/> when nothing here names it.</returns>
    internal string Resolve(string location)
    {
        if (string.IsNullOrEmpty(location)) return null;

        string key = Normalize(location);
        if (_files != null)
        {
            return _files.TryGetValue(key, out string path) ? path : null;
        }

        if (Path.IsPathRooted(key)) return key;
        return string.IsNullOrEmpty(_directory) ? key : Path.Combine(_directory, key);
    }

    private static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return name.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
    }
}

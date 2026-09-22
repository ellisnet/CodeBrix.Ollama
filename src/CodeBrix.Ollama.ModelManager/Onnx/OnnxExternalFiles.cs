using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>Discovers ONNX weight files from tensor metadata, including tensors in nested graphs.</summary>
internal static class OnnxExternalFiles
{
    internal static async Task<IReadOnlyList<string>> LocationsAsync(string path, CancellationToken cancellationToken)
    {
        OnnxModel model = await OnnxModel.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        var locations = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxTensorProto tensor in model.EnumerateTensors())
        {
            if (tensor.HasExternalData)
            {
                string location = tensor.GetExternalDataValue(OnnxModel.ExternalLocationKey);
                if (string.IsNullOrWhiteSpace(location))
                {
                    throw new InvalidDataException("An external ONNX tensor names no weight file: " + tensor.Name);
                }
                locations.Add(location);
            }
        }
        var ordered = new List<string>(locations);
        ordered.Sort(StringComparer.Ordinal);
        return ordered;
    }

    /// <summary>Resolves a graph-relative reference, requiring it to stay inside its bundle.</summary>
    internal static string BundleName(string graphName, string location)
    {
        if (string.IsNullOrWhiteSpace(location) || location[0] == '/' || location[0] == '\\'
            || location.Contains(':') || location.Contains('\\'))
        {
            throw new InvalidDataException("ONNX external data must name a relative bundle path: " + location);
        }
        int slash = graphName.LastIndexOf('/');
        string relative = (slash < 0 ? string.Empty : graphName.Substring(0, slash + 1)) + location;
        var parts = new List<string>();
        foreach (string part in relative.Split('/'))
        {
            if (part == "." || part.Length == 0)
            {
                continue;
            }
            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    throw new InvalidDataException("ONNX external data escapes the bundle: " + location);
                }
                parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                parts.Add(part);
            }
        }
        if (parts.Count == 0)
        {
            throw new InvalidDataException("ONNX external data names a directory: " + location);
        }
        return string.Join('/', parts);
    }

    internal static async Task<Dictionary<string, HashSet<string>>> ReadBundleAsync(
        IReadOnlyList<ResolvedFile> files, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (ResolvedFile file in files)
        {
            names.Add(file.Name);
        }
        foreach (ResolvedFile file in files)
        {
            if (!OnnxExport.IsOnnxFile(file.Name))
            {
                continue;
            }
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (string location in await LocationsAsync(file.BlobPath, cancellationToken).ConfigureAwait(false))
            {
                string name = BundleName(file.Name, location);
                if (!names.Contains(name))
                {
                    throw new InvalidDataException("The ONNX graph '" + file.Name + "' requires missing bundle file '" + name + "'.");
                }
                if (OnnxExport.IsOnnxFile(name))
                {
                    throw new InvalidDataException("An ONNX graph cannot also be an external weight file: " + name);
                }
                referenced.Add(name);
            }
            result.Add(file.Name, referenced);
        }
        return result;
    }

    internal static async Task<IReadOnlyList<string>> PathsAsync(
        string graphPath, CancellationToken cancellationToken, string bundleRoot = null)
    {
        string graph = Path.GetFullPath(graphPath);
        string root = Path.GetFullPath(bundleRoot ?? Path.GetDirectoryName(graph));
        string graphName = Path.GetRelativePath(root, graph).Replace(Path.DirectorySeparatorChar, '/');
        var result = new List<string>();
        foreach (string location in await LocationsAsync(graph, cancellationToken).ConfigureAwait(false))
        {
            string name = BundleName(graphName, location);
            string full = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                throw new FileNotFoundException("An ONNX external weight file is missing.", full);
            }
            result.Add(full);
        }
        return result;
    }

    /// <summary>Keeps a quantizer's new weights separate from weights retained by an unselected graph.</summary>
    internal static async Task<bool> AvoidCollisionsAsync(
        string graphPath, string outputRoot, ISet<string> reserved, CancellationToken cancellationToken)
    {
        // Skip parsing potentially large inline graphs when there is no filename collision at all.
        bool possible = false;
        foreach (string name in reserved)
        {
            if (File.Exists(Path.Combine(outputRoot, name.Replace('/', Path.DirectorySeparatorChar))))
            {
                possible = true;
                break;
            }
        }
        if (!possible)
        {
            return false;
        }
        OnnxModel model = await OnnxModel.ReadAsync(graphPath, cancellationToken).ConfigureAwait(false);
        string graphName = Path.GetRelativePath(outputRoot, graphPath).Replace(Path.DirectorySeparatorChar, '/');
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (OnnxTensorProto tensor in model.EnumerateTensors())
        {
            if (!tensor.HasExternalData)
            {
                continue;
            }
            string oldLocation = tensor.GetExternalDataValue(OnnxModel.ExternalLocationKey);
            string oldName = BundleName(graphName, oldLocation);
            if (!reserved.Contains(oldName))
            {
                continue;
            }
            if (!renamed.TryGetValue(oldName, out string newLocation))
            {
                string newName = oldName + ".reduced";
                while (reserved.Contains(newName) || File.Exists(Path.Combine(outputRoot, newName)))
                {
                    newName += ".reduced";
                }
                string oldPath = Path.Combine(outputRoot, oldName.Replace('/', Path.DirectorySeparatorChar));
                string newPath = Path.Combine(outputRoot, newName.Replace('/', Path.DirectorySeparatorChar));
                File.Move(oldPath, newPath);
                newLocation = Path.GetRelativePath(Path.GetDirectoryName(graphPath), newPath).Replace(Path.DirectorySeparatorChar, '/');
                renamed.Add(oldName, newLocation);
            }
            foreach (OnnxStringStringEntry entry in tensor.ExternalData)
            {
                if (entry.Key == OnnxModel.ExternalLocationKey)
                {
                    entry.Value = newLocation;
                }
            }
        }
        if (renamed.Count != 0)
        {
            await File.WriteAllBytesAsync(graphPath, model.Serialize(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        return false;
    }
}

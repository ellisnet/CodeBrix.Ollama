using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.Core;

/// <summary>
/// One <c>.onnx</c> file: the parsed <see cref="OnnxModelProto"/>, the folder its side files are relative to, and the
/// reading and writing around both. A file that is read and written back unchanged comes out byte for byte identical,
/// because every field the codec does not model is carried through as it arrived.
/// </summary>
internal sealed class OnnxModel
{
    /// <summary>The key of the side-file entry naming the file, relative to the model file's folder.</summary>
    internal const string ExternalLocationKey = "location";

    /// <summary>The key of the side-file entry giving the byte offset of a tensor inside the side file.</summary>
    internal const string ExternalOffsetKey = "offset";

    /// <summary>The key of the side-file entry giving the byte length of a tensor inside the side file.</summary>
    internal const string ExternalLengthKey = "length";

    /// <summary>The key of the optional side-file entry carrying a checksum of a tensor's bytes.</summary>
    internal const string ExternalChecksumKey = "checksum";

    private const int ReadBufferSize = 128 * 1024;

    private OnnxModel(OnnxModelProto proto, string baseDirectory)
    {
        Proto = proto;
        BaseDirectory = baseDirectory;
    }

    /// <summary>The parsed model.</summary>
    internal OnnxModelProto Proto { get; }

    /// <summary>The folder a tensor's side-file location is resolved against, or <see langword="null"/> when unknown.</summary>
    internal string BaseDirectory { get; set; }

    /// <summary>The model's graph, which every caller in this library requires to be present.</summary>
    internal OnnxGraphProto Graph
    {
        get
        {
            if (Proto.Graph == null)
            {
                throw new InvalidDataException("The ONNX model carries no graph.");
            }

            return Proto.Graph;
        }
    }

    /// <summary>Wraps an already-parsed model, for example one the caller built in memory.</summary>
    /// <param name="proto">The model to wrap.</param>
    /// <param name="baseDirectory">The folder side-file locations are resolved against, or <see langword="null"/>.</param>
    /// <returns>The wrapped model.</returns>
    internal static OnnxModel FromProto(OnnxModelProto proto, string baseDirectory)
    {
        if (proto == null)
        {
            throw new ArgumentNullException(nameof(proto));
        }

        return new OnnxModel(proto, baseDirectory);
    }

    /// <summary>Reads a model file, leaving any side-file tensor where it is.</summary>
    /// <param name="path">The model file's path.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The model.</returns>
    internal static async Task<OnnxModel> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A model file path is required.", nameof(path));
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        return new OnnxModel(OnnxModelProto.Parse(bytes), directory);
    }

    /// <summary>Reads a model from a stream.</summary>
    /// <param name="stream">The stream to read to its end.</param>
    /// <param name="baseDirectory">The folder side-file locations are resolved against, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The model.</returns>
    internal static async Task<OnnxModel> ReadAsync(
        Stream stream,
        string baseDirectory,
        CancellationToken cancellationToken = default)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using MemoryStream buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ReadBufferSize, cancellationToken).ConfigureAwait(false);
        return new OnnxModel(OnnxModelProto.Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)), baseDirectory);
    }

    /// <summary>Parses a model that is already in memory.</summary>
    /// <param name="bytes">The encoded model.</param>
    /// <param name="baseDirectory">The folder side-file locations are resolved against, or <see langword="null"/>.</param>
    /// <returns>The model.</returns>
    internal static OnnxModel Parse(ReadOnlySpan<byte> bytes, string baseDirectory) =>
        new OnnxModel(OnnxModelProto.Parse(bytes), baseDirectory);

    /// <summary>
    /// Reads one tensor's bytes in their natural layout, following a side-file reference when there is one.
    /// </summary>
    /// <param name="tensor">The tensor to read.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The tensor's bytes.</returns>
    internal async Task<byte[]> ReadTensorBytesAsync(
        OnnxTensorProto tensor,
        CancellationToken cancellationToken = default)
    {
        if (tensor == null)
        {
            throw new ArgumentNullException(nameof(tensor));
        }

        if (!tensor.HasExternalData)
        {
            return tensor.RawData ?? Array.Empty<byte>();
        }

        string location = tensor.GetExternalDataValue(ExternalLocationKey);
        if (string.IsNullOrEmpty(location))
        {
            throw new InvalidDataException(
                $"The tensor '{tensor.Name}' says its bytes are in a side file but names no location.");
        }

        long offset = ParseExternalNumber(tensor, ExternalOffsetKey, 0);
        long length = ParseExternalNumber(tensor, ExternalLengthKey, -1);
        string file = ResolveExternalPath(location);
        await using FileStream stream = new FileStream(
            file,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ReadBufferSize,
            useAsync: true);
        if (length < 0)
        {
            length = stream.Length - offset;
        }

        if (offset < 0 || length < 0 || offset + length > stream.Length)
        {
            throw new InvalidDataException(
                $"The tensor '{tensor.Name}' points outside the side file '{location}'.");
        }

        stream.Seek(offset, SeekOrigin.Begin);
        byte[] bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    /// <summary>
    /// Pulls every side-file tensor into the model in memory, so the graph can be edited without the side file.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the reads.</param>
    /// <returns>A task that completes when every tensor holds its own bytes.</returns>
    internal async Task LoadExternalDataAsync(CancellationToken cancellationToken = default)
    {
        foreach (OnnxTensorProto tensor in EnumerateTensors())
        {
            if (!tensor.HasExternalData)
            {
                continue;
            }

            tensor.RawData = await ReadTensorBytesAsync(tensor, cancellationToken).ConfigureAwait(false);
            tensor.ClearExternalData();
        }
    }

    /// <summary>Writes the model to a file.</summary>
    /// <param name="path">The model file's path.</param>
    /// <param name="options">How to write it, or <see langword="null"/> for one self-contained file.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task that completes when the file, and any side file, are on disk.</returns>
    internal async Task WriteAsync(
        string path,
        OnnxSaveOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A model file path is required.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (options != null && options.UseExternalData)
        {
            string fileName = string.IsNullOrEmpty(options.ExternalDataFileName)
                ? Path.GetFileName(fullPath) + ".data"
                : options.ExternalDataFileName;
            await WriteExternalDataAsync(directory, fileName, options.SizeThreshold, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await LoadExternalDataAsync(cancellationToken).ConfigureAwait(false);
        }

        byte[] encoded = Serialize();
        await File.WriteAllBytesAsync(fullPath, encoded, cancellationToken).ConfigureAwait(false);
        BaseDirectory = directory;
    }

    /// <summary>Writes the model to a stream, with every tensor's bytes inside it.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task that completes when the bytes are written.</returns>
    internal async Task WriteAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        await LoadExternalDataAsync(cancellationToken).ConfigureAwait(false);
        byte[] encoded = Serialize();
        await stream.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Encodes the model into a new array.</summary>
    /// <returns>The encoded model.</returns>
    internal byte[] Serialize()
    {
        int size = Proto.CalculateSize();
        ProtobufWriter writer = new ProtobufWriter(size);
        Proto.WriteTo(writer);
        if (writer.Length != size)
        {
            throw new InvalidOperationException(
                "The ONNX model wrote a different number of bytes than its size pass measured.");
        }

        return writer.ToArray();
    }

    /// <summary>Walks every tensor in the model, including those inside node attributes and sub-graphs.</summary>
    /// <returns>The tensors, in the order they appear.</returns>
    internal IEnumerable<OnnxTensorProto> EnumerateTensors()
    {
        if (Proto.Graph == null)
        {
            yield break;
        }

        foreach (OnnxTensorProto tensor in EnumerateGraphTensors(Proto.Graph))
        {
            yield return tensor;
        }
    }

    private static IEnumerable<OnnxTensorProto> EnumerateGraphTensors(OnnxGraphProto graph)
    {
        foreach (OnnxTensorProto initializer in graph.Initializers)
        {
            yield return initializer;
        }

        foreach (OnnxNodeProto node in graph.Nodes)
        {
            foreach (OnnxAttributeProto attribute in node.Attributes)
            {
                if (attribute.Tensor != null)
                {
                    yield return attribute.Tensor;
                }

                foreach (OnnxTensorProto tensor in attribute.Tensors)
                {
                    yield return tensor;
                }

                if (attribute.Graph != null)
                {
                    foreach (OnnxTensorProto tensor in EnumerateGraphTensors(attribute.Graph))
                    {
                        yield return tensor;
                    }
                }

                foreach (OnnxGraphProto subGraph in attribute.Graphs)
                {
                    foreach (OnnxTensorProto tensor in EnumerateGraphTensors(subGraph))
                    {
                        yield return tensor;
                    }
                }
            }
        }
    }

    private static long ParseExternalNumber(OnnxTensorProto tensor, string key, long fallback)
    {
        string text = tensor.GetExternalDataValue(key);
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
        {
            throw new InvalidDataException(
                $"The tensor '{tensor.Name}' has a side-file '{key}' entry that is not a number: '{text}'.");
        }

        return value;
    }

    private async Task WriteExternalDataAsync(
        string directory,
        string fileName,
        int sizeThreshold,
        CancellationToken cancellationToken)
    {
        await LoadExternalDataAsync(cancellationToken).ConfigureAwait(false);
        List<OnnxTensorProto> moving = new List<OnnxTensorProto>();
        foreach (OnnxTensorProto tensor in EnumerateTensors())
        {
            if (tensor.RawData != null && tensor.RawData.Length >= sizeThreshold)
            {
                moving.Add(tensor);
            }
        }

        string sidePath = string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
        await using (FileStream side = new FileStream(
            sidePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            ReadBufferSize,
            useAsync: true))
        {
            foreach (OnnxTensorProto tensor in moving)
            {
                long offset = side.Position;
                byte[] bytes = tensor.RawData;
                await side.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                tensor.RawData = null;
                tensor.ExternalData.Clear();
                tensor.ExternalData.Add(OnnxStringStringEntry.Create(ExternalLocationKey, fileName));
                tensor.ExternalData.Add(OnnxStringStringEntry.Create(
                    ExternalOffsetKey,
                    offset.ToString(CultureInfo.InvariantCulture)));
                tensor.ExternalData.Add(OnnxStringStringEntry.Create(
                    ExternalLengthKey,
                    bytes.Length.ToString(CultureInfo.InvariantCulture)));
                tensor.DataLocation = (int)OnnxDataLocation.External;
            }
        }
    }

    private string ResolveExternalPath(string location)
    {
        string normalized = location.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) || string.IsNullOrEmpty(BaseDirectory))
        {
            return normalized;
        }

        return Path.Combine(BaseDirectory, normalized);
    }
}

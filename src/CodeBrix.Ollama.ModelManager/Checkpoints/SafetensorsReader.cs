using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Reads a <c>.safetensors</c> container: eight little-endian bytes of header length, a JSON header naming each
/// tensor's dtype, shape and byte range, then the values, packed back to back.
/// </summary>
/// <remarks>
/// The reader refuses by name rather than guessing: a dtype it does not know, a byte range that runs backwards,
/// overlaps another tensor or reaches past the end of the file, a shape that does not account for the bytes it
/// claims, and a header larger than <see cref="MaxHeaderLength"/>.
/// </remarks>
internal sealed class SafetensorsReader : ICheckpointReader
{
    /// <summary>The largest JSON header the reader accepts; a real one is a few hundred kilobytes.</summary>
    internal const long MaxHeaderLength = 128L << 20;

    private readonly string _path;
    private readonly long _dataStart;
    private readonly IReadOnlyDictionary<string, string> _headerMetadata;

    private SafetensorsReader(string path, long dataStart, IReadOnlyList<CheckpointTensor> tensors,
        IReadOnlyDictionary<string, string> headerMetadata)
    {
        _path = path;
        _dataStart = dataStart;
        _headerMetadata = headerMetadata;
        Tensors = tensors;
    }

    /// <summary>The tensors the header declares, in the order the header declares them.</summary>
    public IReadOnlyList<CheckpointTensor> Tensors { get; }

    /// <summary>The string entries of the header's <c>__metadata__</c> object, empty when there is none.</summary>
    internal IReadOnlyDictionary<string, string> HeaderMetadata
    {
        get { return _headerMetadata; }
    }

    /// <summary>Opens a safetensors file and parses its header.</summary>
    /// <param name="path">The file to open.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The reader.</returns>
    internal static async Task<SafetensorsReader> OpenAsync(string path, CancellationToken cancellationToken)
    {
        byte[] header;
        long dataStart;
        long fileLength;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16,
                   FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            fileLength = stream.Length;
            var lengthBytes = new byte[8];
            await ReadExactAsync(stream, lengthBytes, path, "the header length", cancellationToken)
                .ConfigureAwait(false);
            ulong headerLength = BinaryPrimitives.ReadUInt64LittleEndian(lengthBytes);
            if (headerLength > (ulong)MaxHeaderLength)
            {
                throw new CheckpointFormatException("The safetensors file \"" + path + "\" declares a header of " +
                    headerLength.ToString(CultureInfo.InvariantCulture) + " bytes, more than the maximum of " +
                    MaxHeaderLength.ToString(CultureInfo.InvariantCulture) + ".");
            }

            if ((ulong)fileLength < 8 + headerLength)
            {
                throw new CheckpointFormatException("The safetensors file \"" + path + "\" is " + fileLength +
                    " bytes, too short for the header of " + headerLength.ToString(CultureInfo.InvariantCulture) +
                    " bytes it declares.");
            }

            header = new byte[(int)headerLength];
            await ReadExactAsync(stream, header, path, "the header", cancellationToken).ConfigureAwait(false);
            dataStart = 8 + (long)headerLength;
        }

        var tensors = new List<CheckpointTensor>();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        long dataLength = fileLength - dataStart;
        ParseHeader(path, header, dataLength, tensors, metadata);
        return new SafetensorsReader(path, dataStart, tensors, metadata);
    }

    /// <summary>Opens a stream over one tensor's values.</summary>
    /// <param name="tensor">A descriptor this reader returned.</param>
    /// <param name="cancellationToken">A token that cancels the open.</param>
    /// <returns>A stream of exactly the tensor's bytes.</returns>
    public Task<Stream> OpenTensorAsync(CheckpointTensor tensor, CancellationToken cancellationToken = default)
    {
        if (tensor == null)
        {
            throw new ArgumentNullException(nameof(tensor));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            stream.Seek(_dataStart + tensor.ByteOffset, SeekOrigin.Begin);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        return Task.FromResult<Stream>(new CheckpointRegionStream(stream, tensor.ByteCount));
    }

    /// <summary>Releases the reader. The reader holds no open handle between tensors.</summary>
    public void Dispose()
    {
    }

    private static void ParseHeader(string path, byte[] header, long dataLength, List<CheckpointTensor> tensors,
        Dictionary<string, string> metadata)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(header);
        }
        catch (JsonException exception)
        {
            throw new CheckpointFormatException(
                "The safetensors file \"" + path + "\" does not start with a JSON header.", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new CheckpointFormatException(
                    "The header of the safetensors file \"" + path + "\" is not a JSON object.");
            }

            long previousEnd = 0;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "__metadata__", StringComparison.Ordinal))
                {
                    ReadHeaderMetadata(property.Value, metadata);
                    continue;
                }

                tensors.Add(ReadTensor(path, property, dataLength, ref previousEnd));
            }
        }
    }

    private static void ReadHeaderMetadata(JsonElement element, Dictionary<string, string> metadata)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty entry in element.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String)
            {
                metadata[entry.Name] = entry.Value.GetString();
            }
        }
    }

    private static CheckpointTensor ReadTensor(string path, JsonProperty property, long dataLength,
        ref long previousEnd)
    {
        string name = property.Name;
        if (property.Value.ValueKind != JsonValueKind.Object)
        {
            throw new CheckpointFormatException("The header entry for tensor \"" + name + "\" in \"" + path +
                "\" is not a JSON object.");
        }

        if (!property.Value.TryGetProperty("dtype", out JsonElement dtypeElement)
            || dtypeElement.ValueKind != JsonValueKind.String)
        {
            throw new CheckpointFormatException(
                "The header entry for tensor \"" + name + "\" in \"" + path + "\" has no dtype.");
        }

        string dtypeName = dtypeElement.GetString();
        if (!CheckpointDataTypes.TryParseSafetensors(dtypeName, out CheckpointDataType dataType))
        {
            throw new CheckpointFormatException("Tensor \"" + name + "\" in \"" + path + "\" has dtype \"" +
                dtypeName + "\", which this reader does not support.");
        }

        var shape = new List<long>();
        if (property.Value.TryGetProperty("shape", out JsonElement shapeElement)
            && shapeElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement dimension in shapeElement.EnumerateArray())
            {
                if (dimension.ValueKind != JsonValueKind.Number || !dimension.TryGetInt64(out long value))
                {
                    throw new CheckpointFormatException("Tensor \"" + name + "\" in \"" + path +
                        "\" has a shape entry that is not a whole number.");
                }

                shape.Add(value);
            }
        }
        else
        {
            throw new CheckpointFormatException(
                "The header entry for tensor \"" + name + "\" in \"" + path + "\" has no shape.");
        }

        if (!property.Value.TryGetProperty("data_offsets", out JsonElement offsetsElement)
            || offsetsElement.ValueKind != JsonValueKind.Array || offsetsElement.GetArrayLength() != 2)
        {
            throw new CheckpointFormatException("The header entry for tensor \"" + name + "\" in \"" + path +
                "\" has no pair of data offsets.");
        }

        long start = offsetsElement[0].GetInt64();
        long end = offsetsElement[1].GetInt64();
        if (start < 0 || end < start)
        {
            throw new CheckpointFormatException("Tensor \"" + name + "\" in \"" + path + "\" declares the byte " +
                "range " + start + " to " + end + ", which runs backwards.");
        }

        if (start < previousEnd)
        {
            throw new CheckpointFormatException("Tensor \"" + name + "\" in \"" + path + "\" starts at byte " +
                start + ", inside the range of the tensor before it, which ends at " + previousEnd + ".");
        }

        if (end > dataLength)
        {
            throw new CheckpointFormatException("Tensor \"" + name + "\" in \"" + path + "\" ends at byte " +
                end + " of a data region that is only " + dataLength + " bytes long.");
        }

        var tensor = new CheckpointTensor(name, dataType, shape, start);
        if (tensor.ByteCount != end - start)
        {
            throw new CheckpointFormatException("Tensor \"" + name + "\" in \"" + path + "\" claims " +
                (end - start) + " bytes, but its shape and dtype " + CheckpointDataTypes.GetName(dataType) +
                " account for " + tensor.ByteCount + ".");
        }

        previousEnd = end;
        return tensor;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, string path, string what,
        CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int taken = await stream.ReadAsync(new Memory<byte>(buffer, read, buffer.Length - read),
                cancellationToken).ConfigureAwait(false);
            if (taken == 0)
            {
                throw new CheckpointFormatException(
                    "The safetensors file \"" + path + "\" ends part way through " + what + ".");
            }

            read += taken;
        }
    }
}

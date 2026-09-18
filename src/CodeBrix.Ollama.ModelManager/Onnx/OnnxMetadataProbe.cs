using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Answers one question about a model file without reading the model: does it carry a given metadata entry.
/// </summary>
/// <remarks>
/// <para>
/// Choosing an engine depends on whether a graph has been through shape inference, which a model records as a metadata
/// entry. Reading the whole graph to find that out would mean loading a gigabyte into memory to decide not to use it,
/// so this walks the outermost message only: it reads each field's tag, steps over the graph by its own length, and
/// stops at the metadata entries. What it touches is a few hundred bytes and one seek, whatever the model weighs.
/// </para>
/// <para>
/// A file that is not a model, or is cut short, answers <see langword="false"/> rather than failing: this is a
/// question asked before the work starts, and the engine that then reads the file properly is the one that says what
/// is wrong with it.
/// </para>
/// </remarks>
internal static class OnnxMetadataProbe
{
    /// <summary>The field number of <c>metadata_props</c> in an ONNX <c>ModelProto</c>.</summary>
    private const int MetadataPropertiesField = 14;

    /// <summary>The field number of <c>key</c> in an ONNX <c>StringStringEntryProto</c>.</summary>
    private const int EntryKeyField = 1;

    /// <summary>The field number of <c>value</c> in an ONNX <c>StringStringEntryProto</c>.</summary>
    private const int EntryValueField = 2;

    /// <summary>The largest metadata entry this probe will read, which is far beyond any real one.</summary>
    private const int LargestEntry = 16 * 1024 * 1024;

    /// <summary>
    /// Whether a model file carries a metadata entry with the given key and value.
    /// </summary>
    /// <param name="path">The model file's path.</param>
    /// <param name="key">The metadata key to look for.</param>
    /// <param name="value">The value that key has to have.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns><see langword="true"/> when the file carries exactly that entry.</returns>
    internal static async Task<bool> HasMetadataEntryAsync(
        string path,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            await using FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: false);
            return ReadMetadata(stream, key, value, cancellationToken);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// Walks the outermost message, stepping over everything that is not a metadata entry.
    /// </summary>
    /// <param name="stream">The model file, positioned at its start.</param>
    /// <param name="key">The metadata key to look for.</param>
    /// <param name="value">The value that key has to have.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns><see langword="true"/> when the entry is there.</returns>
    private static bool ReadMetadata(
        Stream stream, string key, string value, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryReadVarint(stream, out ulong tag))
            {
                return false;
            }

            int field = (int)(tag >> 3);
            int wireType = (int)(tag & 0x7);

            switch (wireType)
            {
                case 0:
                    if (!TryReadVarint(stream, out _))
                    {
                        return false;
                    }
                    break;

                case 1:
                    stream.Seek(8, SeekOrigin.Current);
                    break;

                case 5:
                    stream.Seek(4, SeekOrigin.Current);
                    break;

                case 2:
                    if (!TryReadVarint(stream, out ulong length) || length > long.MaxValue)
                    {
                        return false;
                    }

                    if (field == MetadataPropertiesField && length <= LargestEntry)
                    {
                        byte[] body = new byte[(int)length];
                        stream.ReadExactly(body, 0, body.Length);
                        if (IsEntry(body, key, value))
                        {
                            return true;
                        }
                    }
                    else
                    {
                        stream.Seek((long)length, SeekOrigin.Current);
                    }
                    break;

                default:
                    //Groups, which ONNX does not use: nothing sensible is left to read.
                    return false;
            }

            if (stream.Position >= stream.Length)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Whether one metadata entry is the one being looked for.
    /// </summary>
    /// <param name="body">The encoded entry.</param>
    /// <param name="key">The metadata key to look for.</param>
    /// <param name="value">The value that key has to have.</param>
    /// <returns><see langword="true"/> when both match.</returns>
    private static bool IsEntry(ReadOnlySpan<byte> body, string key, string value)
    {
        string foundKey = null;
        string foundValue = null;
        int offset = 0;

        while (offset < body.Length)
        {
            if (!TryReadVarint(body, ref offset, out ulong tag))
            {
                return false;
            }

            int field = (int)(tag >> 3);
            int wireType = (int)(tag & 0x7);

            if (wireType != 2)
            {
                if (wireType == 0 && TryReadVarint(body, ref offset, out _))
                {
                    continue;
                }

                return false;
            }

            if (!TryReadVarint(body, ref offset, out ulong length)
                || length > (ulong)(body.Length - offset))
            {
                return false;
            }

            int size = (int)length;
            if (field == EntryKeyField)
            {
                foundKey = Encoding.UTF8.GetString(body.Slice(offset, size));
            }
            else if (field == EntryValueField)
            {
                foundValue = Encoding.UTF8.GetString(body.Slice(offset, size));
            }

            offset += size;
        }

        return string.Equals(foundKey, key, StringComparison.Ordinal)
            && string.Equals(foundValue, value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads one variable-length integer from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="value">The value read.</param>
    /// <returns><see langword="true"/> when one was read.</returns>
    private static bool TryReadVarint(Stream stream, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            int next = stream.ReadByte();
            if (next < 0)
            {
                return false;
            }

            value |= (ulong)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads one variable-length integer from a buffer.
    /// </summary>
    /// <param name="body">The buffer to read from.</param>
    /// <param name="offset">Where to read, moved past what was read.</param>
    /// <param name="value">The value read.</param>
    /// <returns><see langword="true"/> when one was read.</returns>
    private static bool TryReadVarint(ReadOnlySpan<byte> body, ref int offset, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (offset >= body.Length)
            {
                return false;
            }

            byte next = body[offset++];
            value |= (ulong)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }
}

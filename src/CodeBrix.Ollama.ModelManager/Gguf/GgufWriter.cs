using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: gguf-py/gguf/gguf_writer.py@b10221;

/// <summary>
/// Builds a GGUF version 3 file: the magic and counts, the key-values in the order they were added, one
/// descriptor per tensor, and then the values, each padded up to the file's alignment.
/// </summary>
/// <remarks>
/// <para>
/// The writer is the mirror of <see cref="GgufReader"/> and is deliberately literal about the inference
/// engine's own writer, because what it produces is compared with that writer's output byte for byte: keys keep
/// the order they were added in, an empty string or an empty array is not written at all, and the padding after
/// the last tensor is written like the padding after every other one.
/// </para>
/// <para>
/// It is internal in this version. Whether a GGUF writer becomes part of the public surface is a later
/// decision; what is public today is the conversion that uses it.
/// </para>
/// </remarks>
internal sealed class GgufWriter
{
    /// <summary>The GGUF version this writer produces.</summary>
    internal const uint Version = 3;

    /// <summary>The alignment a file uses when it does not carry <c>general.alignment</c>.</summary>
    internal const long DefaultAlignment = 32;

    private const int CopyBufferSize = 1 << 16;

    private readonly OrderedDictionary<string, GgufValue> _keyValues =
        new OrderedDictionary<string, GgufValue>(StringComparer.Ordinal);

    private readonly List<GgufWriterTensor> _tensors = new List<GgufWriterTensor>();

    /// <summary>Starts a file for one architecture, writing <c>general.architecture</c> as its first key.</summary>
    /// <param name="architecture">The architecture name, for example <c>llama</c>.</param>
    internal GgufWriter(string architecture)
    {
        Alignment = DefaultAlignment;
        AddString("general.architecture", architecture);
    }

    /// <summary>The alignment the data section is padded to.</summary>
    internal long Alignment { get; private set; }

    /// <summary>How many key-values have been added.</summary>
    internal int KeyCount
    {
        get { return _keyValues.Count; }
    }

    /// <summary>How many tensors have been queued.</summary>
    internal int TensorCount
    {
        get { return _tensors.Count; }
    }

    /// <summary>Adds a string. An empty string is not written, which is what the engine's writer does.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddString(string key, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        Add(key, GgufValue.CreateScalar(GgufValueType.String, value));
    }

    /// <summary>Adds an unsigned 8-bit value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddUInt8(string key, byte value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.UInt8, value));
    }

    /// <summary>Adds a signed 8-bit value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddInt8(string key, sbyte value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.Int8, value));
    }

    /// <summary>Adds an unsigned 16-bit value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddUInt16(string key, ushort value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.UInt16, value));
    }

    /// <summary>Adds a signed 16-bit value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddInt16(string key, short value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.Int16, value));
    }

    /// <summary>Adds an unsigned 32-bit value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddUInt32(string key, uint value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.UInt32, value));
    }

    /// <summary>Adds a signed 32-bit value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddInt32(string key, int value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.Int32, value));
    }

    /// <summary>Adds an unsigned 64-bit value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddUInt64(string key, ulong value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.UInt64, value));
    }

    /// <summary>Adds a signed 64-bit value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddInt64(string key, long value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.Int64, value));
    }

    /// <summary>Adds a 32-bit float.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddFloat32(string key, float value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.Float32, value));
    }

    /// <summary>Adds a 64-bit float.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddFloat64(string key, double value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.Float64, value));
    }

    /// <summary>Adds a boolean.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void AddBoolean(string key, bool value)
    {
        Add(key, GgufValue.CreateScalar(GgufValueType.Bool, value));
    }

    /// <summary>Adds an array. An empty array is not written, which is what the engine's writer does.</summary>
    /// <param name="key">The key.</param>
    /// <param name="elementType">The element type every item shares.</param>
    /// <param name="values">The items, as the typed array the element type implies.</param>
    internal void AddArray(string key, GgufValueType elementType, Array values)
    {
        if (values == null || values.Length == 0)
        {
            return;
        }

        Add(key, GgufValue.CreateArray(elementType, values.Length, values));
    }

    /// <summary>Sets the alignment of the data section and records it as <c>general.alignment</c>.</summary>
    /// <param name="alignment">A power of two greater than zero.</param>
    internal void AddCustomAlignment(uint alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment),
                "A GGUF alignment must be a power of two greater than zero.");
        }

        Alignment = alignment;
        AddUInt32("general.alignment", alignment);
    }

    /// <summary>Queues one tensor.</summary>
    /// <param name="tensor">The descriptor and the callback that produces its values.</param>
    internal void AddTensor(GgufWriterTensor tensor)
    {
        if (tensor == null)
        {
            throw new ArgumentNullException(nameof(tensor));
        }

        for (int i = 0; i < _tensors.Count; i++)
        {
            if (string.Equals(_tensors[i].Name, tensor.Name, StringComparison.Ordinal))
            {
                throw new GgufFormatException("The GGUF file would carry the tensor \"" + tensor.Name +
                    "\" twice.");
            }
        }

        _tensors.Add(tensor);
    }

    /// <summary>Writes the whole file.</summary>
    /// <param name="destination">The stream to write to, positioned where the file starts.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>The number of bytes written.</returns>
    internal async Task<long> WriteAsync(Stream destination, CancellationToken cancellationToken)
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        var header = new MemoryStream();
        WriteBytes(header, new byte[] { (byte)'G', (byte)'G', (byte)'U', (byte)'F' });
        WriteUInt32(header, Version);
        WriteUInt64(header, (ulong)_tensors.Count);
        WriteUInt64(header, (ulong)_keyValues.Count);

        foreach (KeyValuePair<string, GgufValue> entry in _keyValues)
        {
            WriteString(header, entry.Key);
            WriteValue(header, entry.Value, true);
        }

        long offset = 0;
        for (int i = 0; i < _tensors.Count; i++)
        {
            GgufWriterTensor tensor = _tensors[i];
            WriteString(header, tensor.Name);
            WriteUInt32(header, (uint)tensor.Shape.Count);
            for (int dimension = 0; dimension < tensor.Shape.Count; dimension++)
            {
                WriteUInt64(header, (ulong)tensor.Shape[dimension]);
            }

            WriteUInt32(header, (uint)tensor.Type);
            WriteUInt64(header, (ulong)offset);
            offset += Pad(tensor.ByteCount, Alignment);
        }

        header.Position = 0;
        await header.CopyToAsync(destination, CopyBufferSize, cancellationToken).ConfigureAwait(false);
        long written = header.Length;
        written += await WritePaddingAsync(destination, written, cancellationToken).ConfigureAwait(false);

        for (int i = 0; i < _tensors.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GgufWriterTensor tensor = _tensors[i];
            await tensor.WriteData(destination, cancellationToken).ConfigureAwait(false);
            written += tensor.ByteCount;
            written += await WritePaddingAsync(destination, tensor.ByteCount, cancellationToken)
                .ConfigureAwait(false);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        return written;
    }

    /// <summary>Rounds a length up to a multiple of an alignment.</summary>
    /// <param name="value">The length.</param>
    /// <param name="alignment">The alignment.</param>
    /// <returns>The padded length.</returns>
    internal static long Pad(long value, long alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }

    private void Add(string key, GgufValue value)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A GGUF key cannot be empty.", nameof(key));
        }

        _keyValues[key] = value;
    }

    private async Task<long> WritePaddingAsync(Stream destination, long written,
        CancellationToken cancellationToken)
    {
        long padding = Pad(written, Alignment) - written;
        if (padding == 0)
        {
            return 0;
        }

        var zeros = new byte[padding];
        await destination.WriteAsync(zeros, cancellationToken).ConfigureAwait(false);
        return padding;
    }

    private static void WriteValue(Stream stream, GgufValue value, bool writeType)
    {
        if (writeType)
        {
            WriteUInt32(stream, (uint)value.Type);
        }

        if (value.IsArray)
        {
            WriteUInt32(stream, (uint)value.ArrayElementType);
            WriteUInt64(stream, (ulong)value.ArrayLength);
            var items = (Array)value.RawValue;
            for (int i = 0; i < items.Length; i++)
            {
                WriteScalar(stream, value.ArrayElementType, items.GetValue(i));
            }

            return;
        }

        WriteScalar(stream, value.Type, value.RawValue);
    }

    private static void WriteScalar(Stream stream, GgufValueType type, object value)
    {
        Span<byte> buffer = stackalloc byte[8];
        switch (type)
        {
            case GgufValueType.UInt8:
                stream.WriteByte((byte)value);
                return;
            case GgufValueType.Int8:
                stream.WriteByte((byte)(sbyte)value);
                return;
            case GgufValueType.Bool:
                stream.WriteByte((bool)value ? (byte)1 : (byte)0);
                return;
            case GgufValueType.UInt16:
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)value);
                stream.Write(buffer.Slice(0, 2));
                return;
            case GgufValueType.Int16:
                BinaryPrimitives.WriteInt16LittleEndian(buffer, (short)value);
                stream.Write(buffer.Slice(0, 2));
                return;
            case GgufValueType.UInt32:
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)value);
                stream.Write(buffer.Slice(0, 4));
                return;
            case GgufValueType.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(buffer, (int)value);
                stream.Write(buffer.Slice(0, 4));
                return;
            case GgufValueType.Float32:
                BinaryPrimitives.WriteSingleLittleEndian(buffer, (float)value);
                stream.Write(buffer.Slice(0, 4));
                return;
            case GgufValueType.UInt64:
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, (ulong)value);
                stream.Write(buffer);
                return;
            case GgufValueType.Int64:
                BinaryPrimitives.WriteInt64LittleEndian(buffer, (long)value);
                stream.Write(buffer);
                return;
            case GgufValueType.Float64:
                BinaryPrimitives.WriteDoubleLittleEndian(buffer, (double)value);
                stream.Write(buffer);
                return;
            case GgufValueType.String:
                WriteString(stream, (string)value);
                return;
            default:
                throw new GgufFormatException("A GGUF value of type " + type + " cannot be written.");
        }
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteUInt64(stream, (ulong)bytes.Length);
        WriteBytes(stream, bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteBytes(Stream stream, byte[] bytes)
    {
        stream.Write(bytes, 0, bytes.Length);
    }
}

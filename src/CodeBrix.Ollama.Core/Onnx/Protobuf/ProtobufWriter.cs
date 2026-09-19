using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace CodeBrix.Ollama.Core;

/// <summary>
/// An append-only Protocol Buffers writer over a growable buffer. Every length prefix comes from the size pass the
/// message classes run first, so nothing is ever written twice or shifted.
/// </summary>
internal sealed class ProtobufWriter
{
    private byte[] _buffer;
    private int _position;

    /// <summary>Creates a writer with a starting buffer.</summary>
    /// <param name="capacity">The number of bytes to reserve up front.</param>
    internal ProtobufWriter(int capacity)
    {
        _buffer = new byte[capacity < 16 ? 16 : capacity];
        _position = 0;
    }

    /// <summary>The number of bytes written so far.</summary>
    internal int Length => _position;

    /// <summary>The bytes written so far.</summary>
    internal ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

    /// <summary>Copies the bytes written so far into a new array.</summary>
    internal byte[] ToArray() => _buffer.AsSpan(0, _position).ToArray();

    /// <summary>Writes the bytes written so far to a stream.</summary>
    /// <param name="stream">The stream to write to.</param>
    internal void CopyTo(Stream stream) => stream.Write(_buffer, 0, _position);

    /// <summary>Writes a field tag.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="wireType">The wire type of the value that follows.</param>
    internal void WriteTag(int fieldNumber, ProtobufWireType wireType) =>
        WriteVarint(((ulong)fieldNumber << 3) | (uint)wireType);

    /// <summary>Writes a base-128 variable-length integer.</summary>
    /// <param name="value">The value to write.</param>
    internal void WriteVarint(ulong value)
    {
        EnsureCapacity(10);
        while (value >= 0x80)
        {
            _buffer[_position] = (byte)(value | 0x80);
            _position++;
            value >>= 7;
        }

        _buffer[_position] = (byte)value;
        _position++;
    }

    /// <summary>Writes a signed 64-bit value as a varint.</summary>
    /// <param name="value">The value to write.</param>
    internal void WriteVarint(long value) => WriteVarint(unchecked((ulong)value));

    /// <summary>Writes a tagged varint field.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="value">The value to write.</param>
    internal void WriteVarintField(int fieldNumber, long value)
    {
        WriteTag(fieldNumber, ProtobufWireType.Varint);
        WriteVarint(value);
    }

    /// <summary>Writes a tagged four-byte field.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="value">The value to write.</param>
    internal void WriteFloatField(int fieldNumber, float value)
    {
        WriteTag(fieldNumber, ProtobufWireType.Fixed32);
        WriteFixed32(value);
    }

    /// <summary>Writes four little-endian bytes holding a single-precision float.</summary>
    /// <param name="value">The value to write.</param>
    internal void WriteFixed32(float value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position, 4), BitConverter.SingleToInt32Bits(value));
        _position += 4;
    }

    /// <summary>Writes eight little-endian bytes holding a double-precision float.</summary>
    /// <param name="value">The value to write.</param>
    internal void WriteFixed64(double value)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position, 8), BitConverter.DoubleToInt64Bits(value));
        _position += 8;
    }

    /// <summary>Writes a tagged UTF-8 string field.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="value">The string to write.</param>
    internal void WriteStringField(int fieldNumber, string value)
    {
        WriteTag(fieldNumber, ProtobufWireType.LengthDelimited);
        int byteCount = value.Length == 0 ? 0 : Encoding.UTF8.GetByteCount(value);
        WriteVarint((ulong)byteCount);
        if (byteCount == 0)
        {
            return;
        }

        EnsureCapacity(byteCount);
        Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_position, byteCount));
        _position += byteCount;
    }

    /// <summary>Writes a tagged length-delimited field.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="value">The payload to write.</param>
    internal void WriteBytesField(int fieldNumber, ReadOnlySpan<byte> value)
    {
        WriteTag(fieldNumber, ProtobufWireType.LengthDelimited);
        WriteVarint((ulong)value.Length);
        WriteRaw(value);
    }

    /// <summary>Writes a tagged nested message, using the length its size pass recorded.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="message">The message to write.</param>
    internal void WriteMessageField(int fieldNumber, IProtobufMessage message)
    {
        WriteTag(fieldNumber, ProtobufWireType.LengthDelimited);
        WriteVarint((ulong)message.CachedSize);
        int expectedEnd = _position + message.CachedSize;
        message.WriteTo(this);
        if (_position != expectedEnd)
        {
            throw new InvalidOperationException(
                "An ONNX message wrote a different number of bytes than its size pass measured.");
        }
    }

    /// <summary>Appends bytes with no tag and no length prefix.</summary>
    /// <param name="value">The bytes to append.</param>
    internal void WriteRaw(ReadOnlySpan<byte> value)
    {
        if (value.Length == 0)
        {
            return;
        }

        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(_position, value.Length));
        _position += value.Length;
    }

    private void EnsureCapacity(int extra)
    {
        if (_position + extra <= _buffer.Length)
        {
            return;
        }

        long wanted = (long)_buffer.Length * 2;
        while (wanted < _position + extra)
        {
            wanted *= 2;
        }

        if (wanted > int.MaxValue)
        {
            wanted = int.MaxValue;
        }

        Array.Resize(ref _buffer, (int)wanted);
    }
}

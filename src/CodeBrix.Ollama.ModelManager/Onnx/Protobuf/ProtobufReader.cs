using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// A forward-only reader over one Protocol Buffers message body. It decodes the wire format directly - varints,
/// fixed-width values and length-delimited payloads - and leaves the meaning of each field to the message class
/// that drives it.
/// </summary>
internal ref struct ProtobufReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    /// <summary>Creates a reader over the bytes of a single message body.</summary>
    /// <param name="buffer">The encoded message, with no length prefix.</param>
    internal ProtobufReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    /// <summary>The offset of the next byte to read.</summary>
    internal readonly int Position => _position;

    /// <summary>Whether every byte of the message body has been consumed.</summary>
    internal readonly bool IsAtEnd => _position >= _buffer.Length;

    /// <summary>
    /// Reads the next field tag, splitting it into its field number and wire type.
    /// </summary>
    /// <param name="fieldNumber">Receives the field number.</param>
    /// <param name="wireType">Receives the wire type.</param>
    /// <returns><see langword="true"/> when a tag was read, <see langword="false"/> at the end of the message.</returns>
    internal bool TryReadTag(out int fieldNumber, out ProtobufWireType wireType)
    {
        if (_position >= _buffer.Length)
        {
            fieldNumber = 0;
            wireType = ProtobufWireType.Varint;
            return false;
        }

        ulong tag = ReadVarint();
        fieldNumber = (int)(tag >> 3);
        wireType = (ProtobufWireType)(tag & 0x7);
        if (fieldNumber <= 0)
        {
            throw new InvalidDataException("The ONNX file contains a Protocol Buffers field with number zero.");
        }

        return true;
    }

    /// <summary>Reads one base-128 variable-length integer.</summary>
    internal ulong ReadVarint()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            if (_position >= _buffer.Length)
            {
                throw new InvalidDataException("The ONNX file ends in the middle of a Protocol Buffers varint.");
            }

            byte current = _buffer[_position];
            _position++;
            result |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift > 63)
            {
                throw new InvalidDataException("The ONNX file contains a Protocol Buffers varint longer than ten bytes.");
            }
        }
    }

    /// <summary>Reads a varint as a signed 64-bit integer.</summary>
    internal long ReadInt64() => unchecked((long)ReadVarint());

    /// <summary>Reads a varint as a signed 32-bit integer, allowing the ten-byte encoding of a negative value.</summary>
    internal int ReadInt32() => unchecked((int)(long)ReadVarint());

    /// <summary>Reads four little-endian bytes as a single-precision float.</summary>
    internal float ReadFloat() => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(TakeFixed(4)));

    /// <summary>Reads eight little-endian bytes as a double-precision float.</summary>
    internal double ReadDouble() => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(TakeFixed(8)));

    /// <summary>Reads a length-delimited payload and returns it without copying.</summary>
    internal ReadOnlySpan<byte> ReadLengthDelimited()
    {
        int length = checked((int)ReadVarint());
        if (length < 0 || _position + length > _buffer.Length)
        {
            throw new InvalidDataException("The ONNX file declares a Protocol Buffers field longer than the data that follows it.");
        }

        ReadOnlySpan<byte> payload = _buffer.Slice(_position, length);
        _position += length;
        return payload;
    }

    /// <summary>Reads a length-delimited payload as UTF-8 text.</summary>
    internal string ReadString() => Encoding.UTF8.GetString(ReadLengthDelimited());

    /// <summary>Reads a length-delimited payload into a new array.</summary>
    internal byte[] ReadBytes() => ReadLengthDelimited().ToArray();

    /// <summary>Advances past the value of a field the caller does not model.</summary>
    /// <param name="wireType">The wire type read from the field's tag.</param>
    internal void SkipValue(ProtobufWireType wireType)
    {
        switch (wireType)
        {
            case ProtobufWireType.Varint:
                ReadVarint();
                return;
            case ProtobufWireType.Fixed64:
                TakeFixed(8);
                return;
            case ProtobufWireType.LengthDelimited:
                ReadLengthDelimited();
                return;
            case ProtobufWireType.Fixed32:
                TakeFixed(4);
                return;
            default:
                throw new InvalidDataException(
                    $"The ONNX file uses the unsupported Protocol Buffers wire type {(int)wireType}.");
        }
    }

    /// <summary>Returns the bytes between two offsets of this reader's buffer.</summary>
    /// <param name="start">The first offset to include.</param>
    /// <param name="end">The offset one past the last byte to include.</param>
    internal readonly ReadOnlySpan<byte> Slice(int start, int end) => _buffer[start..end];

    private ReadOnlySpan<byte> TakeFixed(int count)
    {
        if (_position + count > _buffer.Length)
        {
            throw new InvalidDataException("The ONNX file ends in the middle of a fixed-width Protocol Buffers value.");
        }

        ReadOnlySpan<byte> value = _buffer.Slice(_position, count);
        _position += count;
        return value;
    }
}

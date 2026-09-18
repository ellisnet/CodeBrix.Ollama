using System;
using System.Text;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The encoded length of each Protocol Buffers building block. Message classes add these up in their size pass so
/// the writer can emit every length prefix without a second buffer.
/// </summary>
internal static class ProtobufSizes
{
    /// <summary>The number of bytes a base-128 variable-length integer occupies.</summary>
    /// <param name="value">The value to measure.</param>
    internal static int Varint(ulong value)
    {
        int size = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            size++;
        }

        return size;
    }

    /// <summary>The number of bytes a signed 64-bit varint occupies.</summary>
    /// <param name="value">The value to measure.</param>
    internal static int Varint(long value) => Varint(unchecked((ulong)value));

    /// <summary>The number of bytes a field tag occupies.</summary>
    /// <param name="fieldNumber">The field number.</param>
    internal static int Tag(int fieldNumber) => Varint((ulong)fieldNumber << 3);

    /// <summary>The number of bytes a length-delimited payload occupies, including its length prefix.</summary>
    /// <param name="length">The payload length.</param>
    internal static int LengthDelimited(int length) => Varint((ulong)length) + length;

    /// <summary>The UTF-8 byte count of a string.</summary>
    /// <param name="value">The string to measure.</param>
    internal static int Utf8(string value) => value.Length == 0 ? 0 : Encoding.UTF8.GetByteCount(value);

    /// <summary>The number of bytes a tagged string field occupies.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="value">The string to measure.</param>
    internal static int StringField(int fieldNumber, string value) =>
        Tag(fieldNumber) + LengthDelimited(Utf8(value));

    /// <summary>The number of bytes a tagged length-delimited field occupies.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="length">The payload length.</param>
    internal static int BytesField(int fieldNumber, int length) => Tag(fieldNumber) + LengthDelimited(length);

    /// <summary>The number of bytes a tagged varint field occupies.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="value">The value to measure.</param>
    internal static int VarintField(int fieldNumber, long value) => Tag(fieldNumber) + Varint(value);

    /// <summary>The number of bytes a tagged nested message occupies, using the message's cached size.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="message">The message whose size pass has already run.</param>
    internal static int MessageField(int fieldNumber, IProtobufMessage message) =>
        Tag(fieldNumber) + LengthDelimited(message.CachedSize);

    /// <summary>The number of bytes an unpacked repeated varint field occupies.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="values">The values to measure.</param>
    internal static int UnpackedVarintField(int fieldNumber, ReadOnlySpan<long> values)
    {
        int tag = Tag(fieldNumber);
        int size = 0;
        foreach (long value in values)
        {
            size += tag + Varint(value);
        }

        return size;
    }

    /// <summary>The payload length of a packed repeated varint field, without its tag or length prefix.</summary>
    /// <param name="values">The values to measure.</param>
    internal static int PackedVarintPayload(ReadOnlySpan<long> values)
    {
        int size = 0;
        foreach (long value in values)
        {
            size += Varint(value);
        }

        return size;
    }
}

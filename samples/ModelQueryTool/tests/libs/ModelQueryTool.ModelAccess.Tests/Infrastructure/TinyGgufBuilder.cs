using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ModelQueryTool.ModelAccess.Tests;

/// <summary>
/// Writes the smallest file that is still a GGUF file: the magic, the version, no tensors at all, a
/// handful of key-values and then as many arbitrary bytes as a test wants.
/// </summary>
/// <remarks>
/// <para>
/// The arbitrary bytes sit AFTER the header on purpose. A reader of this format reads forwards and stops
/// once it has the descriptors, so a test that shortens the file leaves a header that still reads and a
/// file that is no longer the size the manifest states - which is exactly the damage a size check is
/// meant to find.
/// </para>
/// </remarks>
public static class TinyGgufBuilder
{
    /// <summary>The GGUF version written, which is the one every current publisher writes.</summary>
    private const uint Version = 3;

    /// <summary>The key-value type code of a UTF-8 string.</summary>
    private const uint StringType = 8;

    /// <summary>The key-value type code of an unsigned 32-bit number.</summary>
    private const uint UInt32Type = 4;

    /// <summary>
    /// Builds a file that stands in for a model's weights.
    /// </summary>
    /// <param name="name">The value of <c>general.name</c>, which is what makes two models differ.</param>
    /// <param name="fillerBytes">How many arbitrary bytes to write after the header.</param>
    /// <returns>The bytes of the file.</returns>
    public static byte[] BuildWeights(string name, int fillerBytes)
    {
        var keyValues = new List<KeyValuePair<string, object>>
        {
            new KeyValuePair<string, object>("general.architecture", "llama"),
            new KeyValuePair<string, object>("general.name", name),
            new KeyValuePair<string, object>("general.file_type", 15u),
            new KeyValuePair<string, object>("llama.block_count", 2u),
            new KeyValuePair<string, object>("llama.context_length", 2048u),
            new KeyValuePair<string, object>("llama.embedding_length", 64u),
            new KeyValuePair<string, object>(
                "tokenizer.chat_template",
                "{% for message in messages %}{{ message.content }}{% endfor %}")
        };

        return Build(keyValues, fillerBytes);
    }

    /// <summary>
    /// Builds a file that stands in for a model's vision projector.
    /// </summary>
    /// <param name="name">The value of <c>general.name</c>.</param>
    /// <param name="fillerBytes">How many arbitrary bytes to write after the header.</param>
    /// <returns>The bytes of the file.</returns>
    public static byte[] BuildProjector(string name, int fillerBytes)
    {
        var keyValues = new List<KeyValuePair<string, object>>
        {
            new KeyValuePair<string, object>("general.architecture", "clip"),
            new KeyValuePair<string, object>("general.type", "projector"),
            new KeyValuePair<string, object>("general.name", name),
            new KeyValuePair<string, object>("clip.vision.block_count", 2u)
        };

        return Build(keyValues, fillerBytes);
    }

    /// <summary>
    /// Writes a header made of the given key-values, then the filler.
    /// </summary>
    /// <param name="keyValues">The key-values, each value either a string or an unsigned 32-bit number.</param>
    /// <param name="fillerBytes">How many arbitrary bytes to write after the header.</param>
    /// <returns>The bytes of the file.</returns>
    private static byte[] Build(List<KeyValuePair<string, object>> keyValues, int fillerBytes)
    {
        using var stream = new MemoryStream();

        stream.Write(Encoding.ASCII.GetBytes("GGUF"), 0, 4);
        WriteUInt32(stream, Version);
        WriteUInt64(stream, 0UL);
        WriteUInt64(stream, (ulong)keyValues.Count);

        foreach (KeyValuePair<string, object> entry in keyValues)
        {
            WriteString(stream, entry.Key);
            if (entry.Value is string text)
            {
                WriteUInt32(stream, StringType);
                WriteString(stream, text);
            }
            else
            {
                WriteUInt32(stream, UInt32Type);
                WriteUInt32(stream, (uint)entry.Value);
            }
        }

        for (int index = 0; index < fillerBytes; index++)
        {
            stream.WriteByte((byte)(index % 251));
        }

        return stream.ToArray();
    }

    /// <summary>Writes a length-prefixed UTF-8 string.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="value">The string.</param>
    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteUInt64(stream, (ulong)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Writes an unsigned 32-bit number, least significant byte first.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="value">The number.</param>
    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>Writes an unsigned 64-bit number, least significant byte first.</summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="value">The number.</param>
    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}

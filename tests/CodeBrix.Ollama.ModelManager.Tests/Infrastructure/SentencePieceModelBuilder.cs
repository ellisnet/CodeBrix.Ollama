using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Builds the bytes of a SentencePiece <c>ModelProto</c> by hand, so that a test can say exactly what a
/// <c>tokenizer.model</c> holds - including the shapes a trainer never produces, such as a piece that names no
/// score, a field the reader does not model, and a truncated message.
/// </summary>
/// <remarks>
/// It writes the Protocol Buffers wire format directly rather than through the library's own codec, so that a
/// test's input is independent of the code under test.
/// </remarks>
internal sealed class SentencePieceModelBuilder
{
    private readonly List<byte> _bytes = new List<byte>();

    /// <summary>Appends one piece with its text, its score and its kind.</summary>
    /// <param name="text">The piece's text.</param>
    /// <param name="score">The score.</param>
    /// <param name="type">The kind, as the schema numbers them.</param>
    /// <returns>This builder.</returns>
    internal SentencePieceModelBuilder Piece(string text, float score, int type)
    {
        var piece = new List<byte>();
        WriteString(piece, 1, text);
        WriteFloat(piece, 2, score);
        WriteVarint(piece, (3 << 3) | 0);
        WriteVarint(piece, (ulong)type);
        WriteMessage(_bytes, 1, piece);
        return this;
    }

    /// <summary>Appends a piece that names only its text, so its score and kind take the schema's defaults.</summary>
    /// <param name="text">The piece's text.</param>
    /// <returns>This builder.</returns>
    internal SentencePieceModelBuilder BarePiece(string text)
    {
        var piece = new List<byte>();
        WriteString(piece, 1, text);
        WriteMessage(_bytes, 1, piece);
        return this;
    }

    /// <summary>Appends a piece carrying an extra field the reader does not model, which it must skip.</summary>
    /// <param name="text">The piece's text.</param>
    /// <param name="score">The score.</param>
    /// <returns>This builder.</returns>
    internal SentencePieceModelBuilder PieceWithUnknownField(string text, float score)
    {
        var piece = new List<byte>();
        WriteString(piece, 1, text);
        WriteFloat(piece, 2, score);
        WriteString(piece, 9, "something this reader does not model");
        WriteMessage(_bytes, 1, piece);
        return this;
    }

    /// <summary>Appends a top-level field the reader does not model, such as the trainer specification.</summary>
    /// <param name="fieldNumber">The field number.</param>
    /// <param name="content">Its content.</param>
    /// <returns>This builder.</returns>
    internal SentencePieceModelBuilder UnknownField(int fieldNumber, string content)
    {
        WriteString(_bytes, fieldNumber, content);
        return this;
    }

    /// <summary>The model's bytes.</summary>
    /// <returns>A new array.</returns>
    internal byte[] ToArray() => _bytes.ToArray();

    /// <summary>The model's bytes with the last one removed, so the file ends part way through a message.</summary>
    /// <returns>A new array.</returns>
    internal byte[] ToTruncatedArray()
    {
        byte[] full = ToArray();
        var truncated = new byte[full.Length - 1];
        Array.Copy(full, truncated, truncated.Length);
        return truncated;
    }

    private static void WriteMessage(List<byte> destination, int fieldNumber, List<byte> body)
    {
        WriteVarint(destination, (ulong)((fieldNumber << 3) | 2));
        WriteVarint(destination, (ulong)body.Count);
        destination.AddRange(body);
    }

    private static void WriteString(List<byte> destination, int fieldNumber, string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        WriteVarint(destination, (ulong)((fieldNumber << 3) | 2));
        WriteVarint(destination, (ulong)encoded.Length);
        destination.AddRange(encoded);
    }

    private static void WriteFloat(List<byte> destination, int fieldNumber, float value)
    {
        WriteVarint(destination, (ulong)((fieldNumber << 3) | 5));
        var buffer = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        destination.AddRange(buffer);
    }

    private static void WriteVarint(List<byte> destination, ulong value)
    {
        while (value >= 0x80)
        {
            destination.Add((byte)(value | 0x80));
            value >>= 7;
        }

        destination.Add((byte)value);
    }
}

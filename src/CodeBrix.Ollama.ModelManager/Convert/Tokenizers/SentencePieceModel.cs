using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/base.py@b10221;

/// <summary>
/// A checkpoint's <c>tokenizer.model</c>: the SentencePiece <c>ModelProto</c>, read for the one field a
/// conversion needs - the repeated list of pieces, each with its text, its score and its kind.
/// </summary>
/// <remarks>
/// <para>
/// This reads the Protocol Buffers wire format with the codec this library already carries, so no package is
/// added and no code from the tokenizer's own project is run. Everything except the pieces - the trainer
/// specification, the normalizer specification and the self-test data - is skipped: a conversion writes the
/// vocabulary into the GGUF file and the engine tokenizes with its own implementation, so the trainer's
/// settings do not travel and are not read.
/// </para>
/// <para>
/// A piece that names no kind is <see cref="SentencePieceTokenType.Normal"/> and a piece that names no score
/// scores zero, which are the defaults the schema gives those fields; both are common in a real file, because
/// a Protocol Buffers writer leaves a default-valued field out.
/// </para>
/// </remarks>
internal sealed class SentencePieceModel
{
    /// <summary>The largest <c>tokenizer.model</c> this reads; a real one is a few megabytes.</summary>
    internal const long MaxFileLength = 128L << 20;

    /// <summary>The file this is read from, which is the name every refusal carries.</summary>
    internal const string FileName = "tokenizer.model";

    private SentencePieceModel(IReadOnlyList<SentencePiecePiece> pieces)
    {
        Pieces = pieces;
    }

    /// <summary>The vocabulary, in identifier order: the piece at index <c>i</c> has identifier <c>i</c>.</summary>
    internal IReadOnlyList<SentencePiecePiece> Pieces { get; }

    /// <summary>Reads the <c>tokenizer.model</c> of a checkpoint directory.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The model.</returns>
    /// <exception cref="CheckpointFormatException">There is no such file, or it is not a model this can read.</exception>
    internal static async Task<SentencePieceModel> LoadAsync(string directory,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, FileName);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new CheckpointFormatException("The file \"" + path + "\" is not there.");
        }

        if (info.Length > MaxFileLength)
        {
            throw new CheckpointFormatException("The file \"" + path + "\" is " +
                info.Length.ToString(CultureInfo.InvariantCulture) + " bytes, more than the maximum of " +
                MaxFileLength.ToString(CultureInfo.InvariantCulture) + " a tokenizer model may be.");
        }

        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(content, path);
    }

    /// <summary>Reads a SentencePiece model from the bytes of a <c>tokenizer.model</c>.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <param name="path">The path, for messages.</param>
    /// <returns>The model.</returns>
    /// <exception cref="CheckpointFormatException">The bytes are not a model this can read.</exception>
    internal static SentencePieceModel Parse(byte[] content, string path)
    {
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var pieces = new List<SentencePiecePiece>();
        try
        {
            var reader = new ProtobufReader(content);
            while (reader.TryReadTag(out int fieldNumber, out ProtobufWireType wireType))
            {
                if (fieldNumber == 1 && wireType == ProtobufWireType.LengthDelimited)
                {
                    pieces.Add(ParsePiece(reader.ReadLengthDelimited()));
                    continue;
                }

                reader.SkipValue(wireType);
            }
        }
        catch (InvalidDataException exception)
        {
            throw new CheckpointFormatException("The file \"" + path +
                "\" is not a SentencePiece model this version can read: " + exception.Message, exception);
        }
        catch (OverflowException exception)
        {
            throw new CheckpointFormatException("The file \"" + path +
                "\" declares a field longer than a tokenizer model can hold.", exception);
        }

        if (pieces.Count == 0)
        {
            throw new CheckpointFormatException(
                "The file \"" + path + "\" holds no pieces, so it carries no vocabulary.");
        }

        return new SentencePieceModel(pieces);
    }

    private static SentencePiecePiece ParsePiece(ReadOnlySpan<byte> body)
    {
        string text = string.Empty;
        float score = 0f;
        SentencePieceTokenType type = SentencePieceTokenType.Normal;

        var reader = new ProtobufReader(body);
        while (reader.TryReadTag(out int fieldNumber, out ProtobufWireType wireType))
        {
            switch (fieldNumber)
            {
                case 1 when wireType == ProtobufWireType.LengthDelimited:
                    text = reader.ReadString();
                    break;
                case 2 when wireType == ProtobufWireType.Fixed32:
                    score = reader.ReadFloat();
                    break;
                case 3 when wireType == ProtobufWireType.Varint:
                    type = (SentencePieceTokenType)reader.ReadInt32();
                    break;
                default:
                    reader.SkipValue(wireType);
                    break;
            }
        }

        return new SentencePiecePiece(text, score, type);
    }
}

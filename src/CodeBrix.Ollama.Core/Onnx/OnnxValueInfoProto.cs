using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.Core;

/// <summary>
/// The ONNX <c>ValueInfoProto</c> message: the name and declared type of a graph input, output or intermediate value.
/// </summary>
internal sealed class OnnxValueInfoProto : IProtobufMessage
{
    private int _cachedSize;

    /// <summary>The value's name, or <see langword="null"/> when the field is absent.</summary>
    internal string Name { get; set; }

    /// <summary>The value's type, or <see langword="null"/> when the field is absent.</summary>
    internal OnnxTypeProto Type { get; set; }

    /// <summary>The value's documentation, or <see langword="null"/> when the field is absent.</summary>
    internal string DocString { get; set; }

    /// <summary>The value's metadata entries.</summary>
    internal List<OnnxStringStringEntry> MetadataProperties { get; } = new List<OnnxStringStringEntry>();

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one value declaration from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed declaration.</returns>
    internal static OnnxValueInfoProto Parse(ReadOnlySpan<byte> body)
    {
        OnnxValueInfoProto value = new OnnxValueInfoProto();
        ProtobufReader reader = new ProtobufReader(body);
        while (true)
        {
            int tagStart = reader.Position;
            if (!reader.TryReadTag(out int fieldNumber, out ProtobufWireType wireType))
            {
                break;
            }

            switch (fieldNumber)
            {
                case 1:
                    value.Name = reader.ReadString();
                    break;
                case 2:
                    value.Type = OnnxTypeProto.Parse(reader.ReadLengthDelimited());
                    break;
                case 3:
                    value.DocString = reader.ReadString();
                    break;
                case 4:
                    value.MetadataProperties.Add(OnnxStringStringEntry.Parse(reader.ReadLengthDelimited()));
                    break;
                default:
                    reader.SkipValue(wireType);
                    value.UnknownFields.Add(fieldNumber, reader.Slice(tagStart, reader.Position));
                    break;
            }
        }

        return value;
    }

    /// <inheritdoc />
    public int CalculateSize()
    {
        int size = 0;
        if (Name != null)
        {
            size += ProtobufSizes.StringField(1, Name);
        }

        if (Type != null)
        {
            Type.CalculateSize();
            size += ProtobufSizes.MessageField(2, Type);
        }

        if (DocString != null)
        {
            size += ProtobufSizes.StringField(3, DocString);
        }

        foreach (OnnxStringStringEntry entry in MetadataProperties)
        {
            entry.CalculateSize();
            size += ProtobufSizes.MessageField(4, entry);
        }

        size += UnknownFields.TotalSize;
        _cachedSize = size;
        return size;
    }

    /// <inheritdoc />
    public void WriteTo(ProtobufWriter writer)
    {
        UnknownFields.BeginWrite();
        if (Name != null)
        {
            UnknownFields.WriteBefore(writer, 1);
            writer.WriteStringField(1, Name);
        }

        if (Type != null)
        {
            UnknownFields.WriteBefore(writer, 2);
            writer.WriteMessageField(2, Type);
        }

        if (DocString != null)
        {
            UnknownFields.WriteBefore(writer, 3);
            writer.WriteStringField(3, DocString);
        }

        if (MetadataProperties.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 4);
        }

        foreach (OnnxStringStringEntry entry in MetadataProperties)
        {
            writer.WriteMessageField(4, entry);
        }

        UnknownFields.WriteRemaining(writer);
    }
}

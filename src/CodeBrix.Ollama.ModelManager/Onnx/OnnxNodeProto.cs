using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The ONNX <c>NodeProto</c> message: one operator call, its input and output names, its attributes and its domain.
/// The device-configuration field is carried through as an unmodelled field.
/// </summary>
internal sealed class OnnxNodeProto : IProtobufMessage
{
    private int _cachedSize;

    /// <summary>The names this node consumes, in order. An empty name means an omitted optional input.</summary>
    internal List<string> Inputs { get; } = new List<string>();

    /// <summary>The names this node produces, in order.</summary>
    internal List<string> Outputs { get; } = new List<string>();

    /// <summary>The node's name, or <see langword="null"/> when the field is absent.</summary>
    internal string Name { get; set; }

    /// <summary>The operator's name, or <see langword="null"/> when the field is absent.</summary>
    internal string OpType { get; set; }

    /// <summary>The node's attributes, in the order they were written.</summary>
    internal List<OnnxAttributeProto> Attributes { get; } = new List<OnnxAttributeProto>();

    /// <summary>The node's documentation, or <see langword="null"/> when the field is absent.</summary>
    internal string DocString { get; set; }

    /// <summary>The operator's domain, or <see langword="null"/> when the field is absent.</summary>
    internal string Domain { get; set; }

    /// <summary>The operator overload's name, or <see langword="null"/> when the field is absent.</summary>
    internal string Overload { get; set; }

    /// <summary>The node's metadata entries.</summary>
    internal List<OnnxStringStringEntry> MetadataProperties { get; } = new List<OnnxStringStringEntry>();

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one node from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed node.</returns>
    internal static OnnxNodeProto Parse(ReadOnlySpan<byte> body)
    {
        OnnxNodeProto value = new OnnxNodeProto();
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
                    value.Inputs.Add(reader.ReadString());
                    break;
                case 2:
                    value.Outputs.Add(reader.ReadString());
                    break;
                case 3:
                    value.Name = reader.ReadString();
                    break;
                case 4:
                    value.OpType = reader.ReadString();
                    break;
                case 5:
                    value.Attributes.Add(OnnxAttributeProto.Parse(reader.ReadLengthDelimited()));
                    break;
                case 6:
                    value.DocString = reader.ReadString();
                    break;
                case 7:
                    value.Domain = reader.ReadString();
                    break;
                case 8:
                    value.Overload = reader.ReadString();
                    break;
                case 9:
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

    /// <summary>Finds one of this node's attributes by name.</summary>
    /// <param name="name">The attribute name to look for.</param>
    /// <returns>The attribute, or <see langword="null"/> when the node has no such attribute.</returns>
    internal OnnxAttributeProto FindAttribute(string name)
    {
        foreach (OnnxAttributeProto attribute in Attributes)
        {
            if (string.Equals(attribute.Name, name, StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }

    /// <summary>Whether any of this node's attributes carries a sub-graph.</summary>
    internal bool HasSubGraphs()
    {
        foreach (OnnxAttributeProto attribute in Attributes)
        {
            if (attribute.HasSubGraph)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public int CalculateSize()
    {
        int size = 0;
        foreach (string input in Inputs)
        {
            size += ProtobufSizes.StringField(1, input);
        }

        foreach (string output in Outputs)
        {
            size += ProtobufSizes.StringField(2, output);
        }

        if (Name != null)
        {
            size += ProtobufSizes.StringField(3, Name);
        }

        if (OpType != null)
        {
            size += ProtobufSizes.StringField(4, OpType);
        }

        foreach (OnnxAttributeProto attribute in Attributes)
        {
            attribute.CalculateSize();
            size += ProtobufSizes.MessageField(5, attribute);
        }

        if (DocString != null)
        {
            size += ProtobufSizes.StringField(6, DocString);
        }

        if (Domain != null)
        {
            size += ProtobufSizes.StringField(7, Domain);
        }

        if (Overload != null)
        {
            size += ProtobufSizes.StringField(8, Overload);
        }

        foreach (OnnxStringStringEntry entry in MetadataProperties)
        {
            entry.CalculateSize();
            size += ProtobufSizes.MessageField(9, entry);
        }

        size += UnknownFields.TotalSize;
        _cachedSize = size;
        return size;
    }

    /// <inheritdoc />
    public void WriteTo(ProtobufWriter writer)
    {
        UnknownFields.BeginWrite();
        if (Inputs.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 1);
            foreach (string input in Inputs)
            {
                writer.WriteStringField(1, input);
            }
        }

        if (Outputs.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 2);
            foreach (string output in Outputs)
            {
                writer.WriteStringField(2, output);
            }
        }

        if (Name != null)
        {
            UnknownFields.WriteBefore(writer, 3);
            writer.WriteStringField(3, Name);
        }

        if (OpType != null)
        {
            UnknownFields.WriteBefore(writer, 4);
            writer.WriteStringField(4, OpType);
        }

        if (Attributes.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 5);
            foreach (OnnxAttributeProto attribute in Attributes)
            {
                writer.WriteMessageField(5, attribute);
            }
        }

        if (DocString != null)
        {
            UnknownFields.WriteBefore(writer, 6);
            writer.WriteStringField(6, DocString);
        }

        if (Domain != null)
        {
            UnknownFields.WriteBefore(writer, 7);
            writer.WriteStringField(7, Domain);
        }

        if (Overload != null)
        {
            UnknownFields.WriteBefore(writer, 8);
            writer.WriteStringField(8, Overload);
        }

        if (MetadataProperties.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 9);
            foreach (OnnxStringStringEntry entry in MetadataProperties)
            {
                writer.WriteMessageField(9, entry);
            }
        }

        UnknownFields.WriteRemaining(writer);
    }
}

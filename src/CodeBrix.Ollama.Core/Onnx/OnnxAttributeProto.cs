using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.Core;

/// <summary>
/// The ONNX <c>AttributeProto</c> message: one named value on a node or a function, with a type discriminator saying
/// which of its value fields is in use. The sparse-tensor branches are carried through as unmodelled fields.
/// </summary>
internal sealed class OnnxAttributeProto : IProtobufMessage
{
    private int _cachedSize;
    private int _intsPayload;

    /// <summary>The attribute's name, or <see langword="null"/> when the field is absent.</summary>
    internal string Name { get; set; }

    /// <summary>The single float value, or <see langword="null"/> when the field is absent.</summary>
    internal float? Float { get; set; }

    /// <summary>The single integer value, or <see langword="null"/> when the field is absent.</summary>
    internal long? Int { get; set; }

    /// <summary>The single string value as raw bytes, or <see langword="null"/> when the field is absent.</summary>
    internal byte[] Bytes { get; set; }

    /// <summary>The single tensor value, or <see langword="null"/> when the field is absent.</summary>
    internal OnnxTensorProto Tensor { get; set; }

    /// <summary>The single graph value, or <see langword="null"/> when the field is absent.</summary>
    internal OnnxGraphProto Graph { get; set; }

    /// <summary>The list of float values.</summary>
    internal List<float> Floats { get; } = new List<float>();

    /// <summary>Whether the float list arrived packed. The schema does not declare it packed.</summary>
    internal bool FloatsPacked { get; set; }

    /// <summary>The list of integer values.</summary>
    internal List<long> Ints { get; } = new List<long>();

    /// <summary>Whether the integer list arrived packed. The schema does not declare it packed.</summary>
    internal bool IntsPacked { get; set; }

    /// <summary>The list of string values as raw bytes.</summary>
    internal List<byte[]> Strings { get; } = new List<byte[]>();

    /// <summary>The list of tensor values.</summary>
    internal List<OnnxTensorProto> Tensors { get; } = new List<OnnxTensorProto>();

    /// <summary>The list of graph values.</summary>
    internal List<OnnxGraphProto> Graphs { get; } = new List<OnnxGraphProto>();

    /// <summary>The attribute's documentation, or <see langword="null"/> when the field is absent.</summary>
    internal string DocString { get; set; }

    /// <summary>The single type value, or <see langword="null"/> when the field is absent.</summary>
    internal OnnxTypeProto TypeProto { get; set; }

    /// <summary>The list of type values.</summary>
    internal List<OnnxTypeProto> TypeProtos { get; } = new List<OnnxTypeProto>();

    /// <summary>The discriminator, or <see langword="null"/> when the field is absent.</summary>
    internal int? AttributeType { get; set; }

    /// <summary>The name of the function attribute this one refers to, or <see langword="null"/> when absent.</summary>
    internal string ReferenceAttributeName { get; set; }

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <summary>Whether this attribute carries a graph, so the node it belongs to has a sub-graph.</summary>
    internal bool HasSubGraph => Graph != null || Graphs.Count > 0;

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one attribute from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed attribute.</returns>
    internal static OnnxAttributeProto Parse(ReadOnlySpan<byte> body)
    {
        OnnxAttributeProto value = new OnnxAttributeProto();
        bool sawFloats = false;
        bool sawInts = false;
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
                    value.Float = reader.ReadFloat();
                    break;
                case 3:
                    value.Int = reader.ReadInt64();
                    break;
                case 4:
                    value.Bytes = reader.ReadBytes();
                    break;
                case 5:
                    value.Tensor = OnnxTensorProto.Parse(reader.ReadLengthDelimited());
                    break;
                case 6:
                    value.Graph = OnnxGraphProto.Parse(reader.ReadLengthDelimited());
                    break;
                case 7:
                    if (!sawFloats)
                    {
                        value.FloatsPacked = wireType == ProtobufWireType.LengthDelimited;
                        sawFloats = true;
                    }

                    if (wireType == ProtobufWireType.LengthDelimited)
                    {
                        ProtobufReader floats = new ProtobufReader(reader.ReadLengthDelimited());
                        while (!floats.IsAtEnd)
                        {
                            value.Floats.Add(floats.ReadFloat());
                        }
                    }
                    else
                    {
                        value.Floats.Add(reader.ReadFloat());
                    }

                    break;
                case 8:
                    if (!sawInts)
                    {
                        value.IntsPacked = wireType == ProtobufWireType.LengthDelimited;
                        sawInts = true;
                    }

                    if (wireType == ProtobufWireType.LengthDelimited)
                    {
                        ProtobufReader ints = new ProtobufReader(reader.ReadLengthDelimited());
                        while (!ints.IsAtEnd)
                        {
                            value.Ints.Add(ints.ReadInt64());
                        }
                    }
                    else
                    {
                        value.Ints.Add(reader.ReadInt64());
                    }

                    break;
                case 9:
                    value.Strings.Add(reader.ReadBytes());
                    break;
                case 10:
                    value.Tensors.Add(OnnxTensorProto.Parse(reader.ReadLengthDelimited()));
                    break;
                case 11:
                    value.Graphs.Add(OnnxGraphProto.Parse(reader.ReadLengthDelimited()));
                    break;
                case 13:
                    value.DocString = reader.ReadString();
                    break;
                case 14:
                    value.TypeProto = OnnxTypeProto.Parse(reader.ReadLengthDelimited());
                    break;
                case 15:
                    value.TypeProtos.Add(OnnxTypeProto.Parse(reader.ReadLengthDelimited()));
                    break;
                case 20:
                    value.AttributeType = reader.ReadInt32();
                    break;
                case 21:
                    value.ReferenceAttributeName = reader.ReadString();
                    break;
                default:
                    reader.SkipValue(wireType);
                    value.UnknownFields.Add(fieldNumber, reader.Slice(tagStart, reader.Position));
                    break;
            }
        }

        return value;
    }

    /// <summary>Creates an attribute holding one integer, the way ONNX's helper does.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">The integer to store.</param>
    /// <returns>The new attribute.</returns>
    internal static OnnxAttributeProto CreateInt(string name, long value) => new OnnxAttributeProto
    {
        Name = name,
        Int = value,
        AttributeType = (int)OnnxAttributeType.Int,
    };

    /// <inheritdoc />
    public int CalculateSize()
    {
        int size = 0;
        if (Name != null)
        {
            size += ProtobufSizes.Tag(1) + ProtobufSizes.LengthDelimited(ProtobufSizes.Utf8(Name));
        }

        if (Float.HasValue)
        {
            size += ProtobufSizes.Tag(2) + 4;
        }

        if (Int.HasValue)
        {
            size += ProtobufSizes.VarintField(3, Int.Value);
        }

        if (Bytes != null)
        {
            size += ProtobufSizes.BytesField(4, Bytes.Length);
        }

        if (Tensor != null)
        {
            Tensor.CalculateSize();
            size += ProtobufSizes.MessageField(5, Tensor);
        }

        if (Graph != null)
        {
            Graph.CalculateSize();
            size += ProtobufSizes.MessageField(6, Graph);
        }

        if (Floats.Count > 0)
        {
            size += FloatsPacked
                ? ProtobufSizes.BytesField(7, Floats.Count * 4)
                : Floats.Count * (ProtobufSizes.Tag(7) + 4);
        }

        _intsPayload = 0;
        if (Ints.Count > 0)
        {
            if (IntsPacked)
            {
                foreach (long element in Ints)
                {
                    _intsPayload += ProtobufSizes.Varint(element);
                }

                size += ProtobufSizes.BytesField(8, _intsPayload);
            }
            else
            {
                foreach (long element in Ints)
                {
                    size += ProtobufSizes.VarintField(8, element);
                }
            }
        }

        foreach (byte[] text in Strings)
        {
            size += ProtobufSizes.BytesField(9, text.Length);
        }

        foreach (OnnxTensorProto tensor in Tensors)
        {
            tensor.CalculateSize();
            size += ProtobufSizes.MessageField(10, tensor);
        }

        foreach (OnnxGraphProto graph in Graphs)
        {
            graph.CalculateSize();
            size += ProtobufSizes.MessageField(11, graph);
        }

        if (DocString != null)
        {
            size += ProtobufSizes.StringField(13, DocString);
        }

        if (TypeProto != null)
        {
            TypeProto.CalculateSize();
            size += ProtobufSizes.MessageField(14, TypeProto);
        }

        foreach (OnnxTypeProto type in TypeProtos)
        {
            type.CalculateSize();
            size += ProtobufSizes.MessageField(15, type);
        }

        if (AttributeType.HasValue)
        {
            size += ProtobufSizes.VarintField(20, AttributeType.Value);
        }

        if (ReferenceAttributeName != null)
        {
            size += ProtobufSizes.StringField(21, ReferenceAttributeName);
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

        if (Float.HasValue)
        {
            UnknownFields.WriteBefore(writer, 2);
            writer.WriteFloatField(2, Float.Value);
        }

        if (Int.HasValue)
        {
            UnknownFields.WriteBefore(writer, 3);
            writer.WriteVarintField(3, Int.Value);
        }

        if (Bytes != null)
        {
            UnknownFields.WriteBefore(writer, 4);
            writer.WriteBytesField(4, Bytes);
        }

        if (Tensor != null)
        {
            UnknownFields.WriteBefore(writer, 5);
            writer.WriteMessageField(5, Tensor);
        }

        if (Graph != null)
        {
            UnknownFields.WriteBefore(writer, 6);
            writer.WriteMessageField(6, Graph);
        }

        if (Floats.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 7);
            if (FloatsPacked)
            {
                writer.WriteTag(7, ProtobufWireType.LengthDelimited);
                writer.WriteVarint((ulong)(Floats.Count * 4));
                foreach (float element in Floats)
                {
                    writer.WriteFixed32(element);
                }
            }
            else
            {
                foreach (float element in Floats)
                {
                    writer.WriteFloatField(7, element);
                }
            }
        }

        if (Ints.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 8);
            if (IntsPacked)
            {
                writer.WriteTag(8, ProtobufWireType.LengthDelimited);
                writer.WriteVarint((ulong)_intsPayload);
                foreach (long element in Ints)
                {
                    writer.WriteVarint(element);
                }
            }
            else
            {
                foreach (long element in Ints)
                {
                    writer.WriteVarintField(8, element);
                }
            }
        }

        if (Strings.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 9);
            foreach (byte[] text in Strings)
            {
                writer.WriteBytesField(9, text);
            }
        }

        if (Tensors.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 10);
            foreach (OnnxTensorProto tensor in Tensors)
            {
                writer.WriteMessageField(10, tensor);
            }
        }

        if (Graphs.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 11);
            foreach (OnnxGraphProto graph in Graphs)
            {
                writer.WriteMessageField(11, graph);
            }
        }

        if (DocString != null)
        {
            UnknownFields.WriteBefore(writer, 13);
            writer.WriteStringField(13, DocString);
        }

        if (TypeProto != null)
        {
            UnknownFields.WriteBefore(writer, 14);
            writer.WriteMessageField(14, TypeProto);
        }

        if (TypeProtos.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 15);
            foreach (OnnxTypeProto type in TypeProtos)
            {
                writer.WriteMessageField(15, type);
            }
        }

        if (AttributeType.HasValue)
        {
            UnknownFields.WriteBefore(writer, 20);
            writer.WriteVarintField(20, AttributeType.Value);
        }

        if (ReferenceAttributeName != null)
        {
            UnknownFields.WriteBefore(writer, 21);
            writer.WriteStringField(21, ReferenceAttributeName);
        }

        UnknownFields.WriteRemaining(writer);
    }
}

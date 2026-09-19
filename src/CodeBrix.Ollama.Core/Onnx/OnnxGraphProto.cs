using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.Core;

/// <summary>
/// The ONNX <c>GraphProto</c> message: the nodes of a computation, its initializers and its declared inputs, outputs
/// and intermediate values. Sparse initializers and quantization annotations are carried through as unmodelled fields.
/// </summary>
internal sealed class OnnxGraphProto : IProtobufMessage
{
    private int _cachedSize;

    /// <summary>The nodes, in the order they were written.</summary>
    internal List<OnnxNodeProto> Nodes { get; } = new List<OnnxNodeProto>();

    /// <summary>The graph's name, or <see langword="null"/> when the field is absent.</summary>
    internal string Name { get; set; }

    /// <summary>The constant tensors the graph owns.</summary>
    internal List<OnnxTensorProto> Initializers { get; } = new List<OnnxTensorProto>();

    /// <summary>The graph's documentation, or <see langword="null"/> when the field is absent.</summary>
    internal string DocString { get; set; }

    /// <summary>The graph's declared inputs.</summary>
    internal List<OnnxValueInfoProto> Inputs { get; } = new List<OnnxValueInfoProto>();

    /// <summary>The graph's declared outputs.</summary>
    internal List<OnnxValueInfoProto> Outputs { get; } = new List<OnnxValueInfoProto>();

    /// <summary>The declared types of the graph's intermediate values.</summary>
    internal List<OnnxValueInfoProto> ValueInfos { get; } = new List<OnnxValueInfoProto>();

    /// <summary>The graph's metadata entries.</summary>
    internal List<OnnxStringStringEntry> MetadataProperties { get; } = new List<OnnxStringStringEntry>();

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one graph from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed graph.</returns>
    internal static OnnxGraphProto Parse(ReadOnlySpan<byte> body)
    {
        OnnxGraphProto value = new OnnxGraphProto();
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
                    value.Nodes.Add(OnnxNodeProto.Parse(reader.ReadLengthDelimited()));
                    break;
                case 2:
                    value.Name = reader.ReadString();
                    break;
                case 5:
                    value.Initializers.Add(OnnxTensorProto.Parse(reader.ReadLengthDelimited()));
                    break;
                case 10:
                    value.DocString = reader.ReadString();
                    break;
                case 11:
                    value.Inputs.Add(OnnxValueInfoProto.Parse(reader.ReadLengthDelimited()));
                    break;
                case 12:
                    value.Outputs.Add(OnnxValueInfoProto.Parse(reader.ReadLengthDelimited()));
                    break;
                case 13:
                    value.ValueInfos.Add(OnnxValueInfoProto.Parse(reader.ReadLengthDelimited()));
                    break;
                case 16:
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

    /// <summary>Finds one of this graph's initializers by name.</summary>
    /// <param name="name">The initializer name to look for.</param>
    /// <returns>The initializer, or <see langword="null"/> when the graph has no such initializer.</returns>
    internal OnnxTensorProto FindInitializer(string name)
    {
        foreach (OnnxTensorProto initializer in Initializers)
        {
            if (string.Equals(initializer.Name, name, StringComparison.Ordinal))
            {
                return initializer;
            }
        }

        return null;
    }

    /// <summary>Finds one of this graph's declared inputs by name.</summary>
    /// <param name="name">The input name to look for.</param>
    /// <returns>The declaration, or <see langword="null"/> when the graph has no such input.</returns>
    internal OnnxValueInfoProto FindInput(string name)
    {
        foreach (OnnxValueInfoProto input in Inputs)
        {
            if (string.Equals(input.Name, name, StringComparison.Ordinal))
            {
                return input;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public int CalculateSize()
    {
        int size = 0;
        foreach (OnnxNodeProto node in Nodes)
        {
            node.CalculateSize();
            size += ProtobufSizes.MessageField(1, node);
        }

        if (Name != null)
        {
            size += ProtobufSizes.StringField(2, Name);
        }

        foreach (OnnxTensorProto initializer in Initializers)
        {
            initializer.CalculateSize();
            size += ProtobufSizes.MessageField(5, initializer);
        }

        if (DocString != null)
        {
            size += ProtobufSizes.StringField(10, DocString);
        }

        foreach (OnnxValueInfoProto input in Inputs)
        {
            input.CalculateSize();
            size += ProtobufSizes.MessageField(11, input);
        }

        foreach (OnnxValueInfoProto output in Outputs)
        {
            output.CalculateSize();
            size += ProtobufSizes.MessageField(12, output);
        }

        foreach (OnnxValueInfoProto valueInfo in ValueInfos)
        {
            valueInfo.CalculateSize();
            size += ProtobufSizes.MessageField(13, valueInfo);
        }

        foreach (OnnxStringStringEntry entry in MetadataProperties)
        {
            entry.CalculateSize();
            size += ProtobufSizes.MessageField(16, entry);
        }

        size += UnknownFields.TotalSize;
        _cachedSize = size;
        return size;
    }

    /// <inheritdoc />
    public void WriteTo(ProtobufWriter writer)
    {
        UnknownFields.BeginWrite();
        if (Nodes.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 1);
            foreach (OnnxNodeProto node in Nodes)
            {
                writer.WriteMessageField(1, node);
            }
        }

        if (Name != null)
        {
            UnknownFields.WriteBefore(writer, 2);
            writer.WriteStringField(2, Name);
        }

        if (Initializers.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 5);
            foreach (OnnxTensorProto initializer in Initializers)
            {
                writer.WriteMessageField(5, initializer);
            }
        }

        if (DocString != null)
        {
            UnknownFields.WriteBefore(writer, 10);
            writer.WriteStringField(10, DocString);
        }

        if (Inputs.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 11);
            foreach (OnnxValueInfoProto input in Inputs)
            {
                writer.WriteMessageField(11, input);
            }
        }

        if (Outputs.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 12);
            foreach (OnnxValueInfoProto output in Outputs)
            {
                writer.WriteMessageField(12, output);
            }
        }

        if (ValueInfos.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 13);
            foreach (OnnxValueInfoProto valueInfo in ValueInfos)
            {
                writer.WriteMessageField(13, valueInfo);
            }
        }

        if (MetadataProperties.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 16);
            foreach (OnnxStringStringEntry entry in MetadataProperties)
            {
                writer.WriteMessageField(16, entry);
            }
        }

        UnknownFields.WriteRemaining(writer);
    }
}

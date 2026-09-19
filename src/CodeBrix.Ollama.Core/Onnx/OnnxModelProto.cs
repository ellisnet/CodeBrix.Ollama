using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.Core;

/// <summary>
/// The ONNX <c>ModelProto</c> message: the top level of an <c>.onnx</c> file. Training information, functions and
/// device configurations are carried through as unmodelled fields.
/// </summary>
internal sealed class OnnxModelProto : IProtobufMessage
{
    private int _cachedSize;

    /// <summary>The IR version, or <see langword="null"/> when the field is absent.</summary>
    internal long? IrVersion { get; set; }

    /// <summary>The producing tool's name, or <see langword="null"/> when the field is absent.</summary>
    internal string ProducerName { get; set; }

    /// <summary>The producing tool's version, or <see langword="null"/> when the field is absent.</summary>
    internal string ProducerVersion { get; set; }

    /// <summary>The model's namespace, or <see langword="null"/> when the field is absent.</summary>
    internal string Domain { get; set; }

    /// <summary>The model's version, or <see langword="null"/> when the field is absent.</summary>
    internal long? ModelVersion { get; set; }

    /// <summary>The model's documentation, or <see langword="null"/> when the field is absent.</summary>
    internal string DocString { get; set; }

    /// <summary>The model's graph, or <see langword="null"/> when the field is absent.</summary>
    internal OnnxGraphProto Graph { get; set; }

    /// <summary>The operator sets the model imports.</summary>
    internal List<OnnxOperatorSetId> OpsetImports { get; } = new List<OnnxOperatorSetId>();

    /// <summary>The model's metadata entries.</summary>
    internal List<OnnxStringStringEntry> MetadataProperties { get; } = new List<OnnxStringStringEntry>();

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one model from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed model.</returns>
    internal static OnnxModelProto Parse(ReadOnlySpan<byte> body)
    {
        OnnxModelProto value = new OnnxModelProto();
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
                    value.IrVersion = reader.ReadInt64();
                    break;
                case 2:
                    value.ProducerName = reader.ReadString();
                    break;
                case 3:
                    value.ProducerVersion = reader.ReadString();
                    break;
                case 4:
                    value.Domain = reader.ReadString();
                    break;
                case 5:
                    value.ModelVersion = reader.ReadInt64();
                    break;
                case 6:
                    value.DocString = reader.ReadString();
                    break;
                case 7:
                    value.Graph = OnnxGraphProto.Parse(reader.ReadLengthDelimited());
                    break;
                case 8:
                    value.OpsetImports.Add(OnnxOperatorSetId.Parse(reader.ReadLengthDelimited()));
                    break;
                case 14:
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

    /// <summary>Reads the value of one of the model's metadata entries.</summary>
    /// <param name="key">The entry key to look for.</param>
    /// <returns>The entry's value, or <see langword="null"/> when the model has no such entry.</returns>
    internal string GetMetadataValue(string key)
    {
        foreach (OnnxStringStringEntry entry in MetadataProperties)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public int CalculateSize()
    {
        int size = 0;
        if (IrVersion.HasValue)
        {
            size += ProtobufSizes.VarintField(1, IrVersion.Value);
        }

        if (ProducerName != null)
        {
            size += ProtobufSizes.StringField(2, ProducerName);
        }

        if (ProducerVersion != null)
        {
            size += ProtobufSizes.StringField(3, ProducerVersion);
        }

        if (Domain != null)
        {
            size += ProtobufSizes.StringField(4, Domain);
        }

        if (ModelVersion.HasValue)
        {
            size += ProtobufSizes.VarintField(5, ModelVersion.Value);
        }

        if (DocString != null)
        {
            size += ProtobufSizes.StringField(6, DocString);
        }

        if (Graph != null)
        {
            Graph.CalculateSize();
            size += ProtobufSizes.MessageField(7, Graph);
        }

        foreach (OnnxOperatorSetId opset in OpsetImports)
        {
            opset.CalculateSize();
            size += ProtobufSizes.MessageField(8, opset);
        }

        foreach (OnnxStringStringEntry entry in MetadataProperties)
        {
            entry.CalculateSize();
            size += ProtobufSizes.MessageField(14, entry);
        }

        size += UnknownFields.TotalSize;
        _cachedSize = size;
        return size;
    }

    /// <inheritdoc />
    public void WriteTo(ProtobufWriter writer)
    {
        UnknownFields.BeginWrite();
        if (IrVersion.HasValue)
        {
            UnknownFields.WriteBefore(writer, 1);
            writer.WriteVarintField(1, IrVersion.Value);
        }

        if (ProducerName != null)
        {
            UnknownFields.WriteBefore(writer, 2);
            writer.WriteStringField(2, ProducerName);
        }

        if (ProducerVersion != null)
        {
            UnknownFields.WriteBefore(writer, 3);
            writer.WriteStringField(3, ProducerVersion);
        }

        if (Domain != null)
        {
            UnknownFields.WriteBefore(writer, 4);
            writer.WriteStringField(4, Domain);
        }

        if (ModelVersion.HasValue)
        {
            UnknownFields.WriteBefore(writer, 5);
            writer.WriteVarintField(5, ModelVersion.Value);
        }

        if (DocString != null)
        {
            UnknownFields.WriteBefore(writer, 6);
            writer.WriteStringField(6, DocString);
        }

        if (Graph != null)
        {
            UnknownFields.WriteBefore(writer, 7);
            writer.WriteMessageField(7, Graph);
        }

        if (OpsetImports.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 8);
            foreach (OnnxOperatorSetId opset in OpsetImports)
            {
                writer.WriteMessageField(8, opset);
            }
        }

        if (MetadataProperties.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 14);
            foreach (OnnxStringStringEntry entry in MetadataProperties)
            {
                writer.WriteMessageField(14, entry);
            }
        }

        UnknownFields.WriteRemaining(writer);
    }
}

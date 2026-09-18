using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The ONNX <c>OperatorSetIdProto</c> message: the domain and version of one operator set a model imports.
/// </summary>
internal sealed class OnnxOperatorSetId : IProtobufMessage
{
    private int _cachedSize;

    /// <summary>The operator set's domain, or <see langword="null"/> when the field is absent.</summary>
    internal string Domain { get; set; }

    /// <summary>The operator set's version, or <see langword="null"/> when the field is absent.</summary>
    internal long? Version { get; set; }

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one operator set identifier from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed identifier.</returns>
    internal static OnnxOperatorSetId Parse(ReadOnlySpan<byte> body)
    {
        OnnxOperatorSetId value = new OnnxOperatorSetId();
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
                    value.Domain = reader.ReadString();
                    break;
                case 2:
                    value.Version = reader.ReadInt64();
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
        if (Domain != null)
        {
            size += ProtobufSizes.StringField(1, Domain);
        }

        if (Version.HasValue)
        {
            size += ProtobufSizes.VarintField(2, Version.Value);
        }

        size += UnknownFields.TotalSize;
        _cachedSize = size;
        return size;
    }

    /// <inheritdoc />
    public void WriteTo(ProtobufWriter writer)
    {
        UnknownFields.BeginWrite();
        if (Domain != null)
        {
            UnknownFields.WriteBefore(writer, 1);
            writer.WriteStringField(1, Domain);
        }

        if (Version.HasValue)
        {
            UnknownFields.WriteBefore(writer, 2);
            writer.WriteVarintField(2, Version.Value);
        }

        UnknownFields.WriteRemaining(writer);
    }
}

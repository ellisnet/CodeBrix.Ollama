using System;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The ONNX <c>TypeProto</c> message. The tensor branch of its union is modelled; the sequence, map, optional and
/// sparse-tensor branches are carried through as unmodelled fields.
/// </summary>
internal sealed class OnnxTypeProto : IProtobufMessage
{
    private int _cachedSize;

    /// <summary>The tensor branch of the union, or <see langword="null"/> when another branch is in use.</summary>
    internal OnnxTensorTypeProto TensorType { get; set; }

    /// <summary>The type's standard denotation, or <see langword="null"/> when the field is absent.</summary>
    internal string Denotation { get; set; }

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one type from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed type.</returns>
    internal static OnnxTypeProto Parse(ReadOnlySpan<byte> body)
    {
        OnnxTypeProto value = new OnnxTypeProto();
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
                    value.TensorType = OnnxTensorTypeProto.Parse(reader.ReadLengthDelimited());
                    break;
                case 6:
                    value.Denotation = reader.ReadString();
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
        if (TensorType != null)
        {
            TensorType.CalculateSize();
            size += ProtobufSizes.MessageField(1, TensorType);
        }

        if (Denotation != null)
        {
            size += ProtobufSizes.StringField(6, Denotation);
        }

        size += UnknownFields.TotalSize;
        _cachedSize = size;
        return size;
    }

    /// <inheritdoc />
    public void WriteTo(ProtobufWriter writer)
    {
        UnknownFields.BeginWrite();
        if (TensorType != null)
        {
            UnknownFields.WriteBefore(writer, 1);
            writer.WriteMessageField(1, TensorType);
        }

        if (Denotation != null)
        {
            UnknownFields.WriteBefore(writer, 6);
            writer.WriteStringField(6, Denotation);
        }

        UnknownFields.WriteRemaining(writer);
    }
}

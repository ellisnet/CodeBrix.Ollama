using System;

namespace CodeBrix.Ollama.Core;

/// <summary>
/// The ONNX <c>TensorShapeProto.Dimension</c> message: one axis, either a fixed size or a symbolic name.
/// </summary>
internal sealed class OnnxTensorShapeDimension : IProtobufMessage
{
    private int _cachedSize;

    /// <summary>The axis length, or <see langword="null"/> when the axis is symbolic or unset.</summary>
    internal long? DimensionValue { get; set; }

    /// <summary>The axis's symbolic name, or <see langword="null"/> when the axis is fixed or unset.</summary>
    internal string DimensionParameter { get; set; }

    /// <summary>The axis's standard denotation, or <see langword="null"/> when the field is absent.</summary>
    internal string Denotation { get; set; }

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one dimension from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed dimension.</returns>
    internal static OnnxTensorShapeDimension Parse(ReadOnlySpan<byte> body)
    {
        OnnxTensorShapeDimension value = new OnnxTensorShapeDimension();
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
                    value.DimensionValue = reader.ReadInt64();
                    break;
                case 2:
                    value.DimensionParameter = reader.ReadString();
                    break;
                case 3:
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
        if (DimensionValue.HasValue)
        {
            size += ProtobufSizes.VarintField(1, DimensionValue.Value);
        }

        if (DimensionParameter != null)
        {
            size += ProtobufSizes.StringField(2, DimensionParameter);
        }

        if (Denotation != null)
        {
            size += ProtobufSizes.StringField(3, Denotation);
        }

        size += UnknownFields.TotalSize;
        _cachedSize = size;
        return size;
    }

    /// <inheritdoc />
    public void WriteTo(ProtobufWriter writer)
    {
        UnknownFields.BeginWrite();
        if (DimensionValue.HasValue)
        {
            UnknownFields.WriteBefore(writer, 1);
            writer.WriteVarintField(1, DimensionValue.Value);
        }

        if (DimensionParameter != null)
        {
            UnknownFields.WriteBefore(writer, 2);
            writer.WriteStringField(2, DimensionParameter);
        }

        if (Denotation != null)
        {
            UnknownFields.WriteBefore(writer, 3);
            writer.WriteStringField(3, Denotation);
        }

        UnknownFields.WriteRemaining(writer);
    }
}

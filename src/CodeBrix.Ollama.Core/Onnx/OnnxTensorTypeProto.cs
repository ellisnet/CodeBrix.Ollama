using System;

namespace CodeBrix.Ollama.Core;

/// <summary>
/// The ONNX <c>TypeProto.Tensor</c> message: the element type and, when known, the shape of a tensor value.
/// </summary>
internal sealed class OnnxTensorTypeProto : IProtobufMessage
{
    private int _cachedSize;

    /// <summary>The element type, or <see langword="null"/> when the field is absent.</summary>
    internal int? ElementType { get; set; }

    /// <summary>The shape, or <see langword="null"/> when the shape is unknown.</summary>
    internal OnnxTensorShapeProto Shape { get; set; }

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one tensor type from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed tensor type.</returns>
    internal static OnnxTensorTypeProto Parse(ReadOnlySpan<byte> body)
    {
        OnnxTensorTypeProto value = new OnnxTensorTypeProto();
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
                    value.ElementType = reader.ReadInt32();
                    break;
                case 2:
                    value.Shape = OnnxTensorShapeProto.Parse(reader.ReadLengthDelimited());
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
        if (ElementType.HasValue)
        {
            size += ProtobufSizes.VarintField(1, ElementType.Value);
        }

        if (Shape != null)
        {
            Shape.CalculateSize();
            size += ProtobufSizes.MessageField(2, Shape);
        }

        size += UnknownFields.TotalSize;
        _cachedSize = size;
        return size;
    }

    /// <inheritdoc />
    public void WriteTo(ProtobufWriter writer)
    {
        UnknownFields.BeginWrite();
        if (ElementType.HasValue)
        {
            UnknownFields.WriteBefore(writer, 1);
            writer.WriteVarintField(1, ElementType.Value);
        }

        if (Shape != null)
        {
            UnknownFields.WriteBefore(writer, 2);
            writer.WriteMessageField(2, Shape);
        }

        UnknownFields.WriteRemaining(writer);
    }
}

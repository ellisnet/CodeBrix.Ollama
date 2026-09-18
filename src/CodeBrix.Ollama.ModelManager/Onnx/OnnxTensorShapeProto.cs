using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The ONNX <c>TensorShapeProto</c> message: the ordered axes of a tensor type.
/// </summary>
internal sealed class OnnxTensorShapeProto : IProtobufMessage
{
    private int _cachedSize;

    /// <summary>The axes, outermost first.</summary>
    internal List<OnnxTensorShapeDimension> Dimensions { get; } = new List<OnnxTensorShapeDimension>();

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one shape from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed shape.</returns>
    internal static OnnxTensorShapeProto Parse(ReadOnlySpan<byte> body)
    {
        OnnxTensorShapeProto value = new OnnxTensorShapeProto();
        ProtobufReader reader = new ProtobufReader(body);
        while (true)
        {
            int tagStart = reader.Position;
            if (!reader.TryReadTag(out int fieldNumber, out ProtobufWireType wireType))
            {
                break;
            }

            if (fieldNumber == 1)
            {
                value.Dimensions.Add(OnnxTensorShapeDimension.Parse(reader.ReadLengthDelimited()));
            }
            else
            {
                reader.SkipValue(wireType);
                value.UnknownFields.Add(fieldNumber, reader.Slice(tagStart, reader.Position));
            }
        }

        return value;
    }

    /// <inheritdoc />
    public int CalculateSize()
    {
        int size = 0;
        foreach (OnnxTensorShapeDimension dimension in Dimensions)
        {
            dimension.CalculateSize();
            size += ProtobufSizes.MessageField(1, dimension);
        }

        size += UnknownFields.TotalSize;
        _cachedSize = size;
        return size;
    }

    /// <inheritdoc />
    public void WriteTo(ProtobufWriter writer)
    {
        UnknownFields.BeginWrite();
        if (Dimensions.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 1);
        }

        foreach (OnnxTensorShapeDimension dimension in Dimensions)
        {
            writer.WriteMessageField(1, dimension);
        }

        UnknownFields.WriteRemaining(writer);
    }
}

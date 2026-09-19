using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.Core;

/// <summary>
/// The ONNX <c>TensorProto</c> message: a dense tensor's shape, element type and bytes. The bytes may sit in
/// <see cref="RawData"/>, in one of the typed repeated fields, or in a side file described by
/// <see cref="ExternalData"/>.
/// </summary>
/// <remarks>
/// Whether a repeated scalar field arrived packed is remembered per field, because the ONNX schema is proto2 and
/// declares only some of them packed. Writing back what was read is what keeps an untouched file byte for byte
/// identical.
/// </remarks>
internal sealed class OnnxTensorProto : IProtobufMessage
{
    private int _cachedSize;
    private int _floatDataPayload;
    private int _int32DataPayload;
    private int _doubleDataPayload;
    private int _uint64DataPayload;

    /// <summary>The tensor's shape.</summary>
    internal List<long> Dimensions { get; } = new List<long>();

    /// <summary>Whether the shape arrived packed. The schema does not declare it packed, so it normally did not.</summary>
    internal bool DimensionsPacked { get; set; }

    /// <summary>The element type, or <see langword="null"/> when the field is absent.</summary>
    internal int? DataType { get; set; }

    /// <summary>The float elements, when the tensor uses the typed field rather than raw bytes.</summary>
    internal List<float> FloatData { get; } = new List<float>();

    /// <summary>Whether the float elements arrived packed. The schema declares them packed.</summary>
    internal bool FloatDataPacked { get; set; } = true;

    /// <summary>The 32-bit integer elements, which also carry the 16-bit, 8-bit, 4-bit and boolean types.</summary>
    internal List<int> Int32Data { get; } = new List<int>();

    /// <summary>Whether the 32-bit integer elements arrived packed. The schema declares them packed.</summary>
    internal bool Int32DataPacked { get; set; } = true;

    /// <summary>The string elements.</summary>
    internal List<byte[]> StringData { get; } = new List<byte[]>();

    /// <summary>The 64-bit integer elements.</summary>
    internal List<long> Int64Data { get; } = new List<long>();

    /// <summary>Whether the 64-bit integer elements arrived packed. The schema declares them packed.</summary>
    internal bool Int64DataPacked { get; set; } = true;

    /// <summary>The tensor's name, or <see langword="null"/> when the field is absent.</summary>
    internal string Name { get; set; }

    /// <summary>The tensor's bytes in their natural layout, or <see langword="null"/> when the field is absent.</summary>
    internal byte[] RawData { get; set; }

    /// <summary>The double elements.</summary>
    internal List<double> DoubleData { get; } = new List<double>();

    /// <summary>Whether the double elements arrived packed. The schema declares them packed.</summary>
    internal bool DoubleDataPacked { get; set; } = true;

    /// <summary>The unsigned 64-bit elements, which also carry the unsigned 32-bit type.</summary>
    internal List<ulong> UInt64Data { get; } = new List<ulong>();

    /// <summary>Whether the unsigned 64-bit elements arrived packed. The schema declares them packed.</summary>
    internal bool UInt64DataPacked { get; set; } = true;

    /// <summary>The tensor's documentation, or <see langword="null"/> when the field is absent.</summary>
    internal string DocString { get; set; }

    /// <summary>The side-file entries - location, offset, length and checksum - when the bytes live outside the model.</summary>
    internal List<OnnxStringStringEntry> ExternalData { get; } = new List<OnnxStringStringEntry>();

    /// <summary>Where the bytes live, or <see langword="null"/> when the field is absent.</summary>
    internal int? DataLocation { get; set; }

    /// <summary>The tensor's metadata entries.</summary>
    internal List<OnnxStringStringEntry> MetadataProperties { get; } = new List<OnnxStringStringEntry>();

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <summary>Whether the tensor's bytes are in a side file.</summary>
    internal bool HasExternalData =>
        DataLocation.HasValue && DataLocation.Value == (int)OnnxDataLocation.External;

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one tensor from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed tensor.</returns>
    internal static OnnxTensorProto Parse(ReadOnlySpan<byte> body)
    {
        OnnxTensorProto value = new OnnxTensorProto();
        bool sawDims = false;
        bool sawFloats = false;
        bool sawInt32 = false;
        bool sawInt64 = false;
        bool sawDoubles = false;
        bool sawUInt64 = false;
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
                    if (!sawDims)
                    {
                        value.DimensionsPacked = wireType == ProtobufWireType.LengthDelimited;
                        sawDims = true;
                    }

                    ReadInt64Field(ref reader, wireType, value.Dimensions);
                    break;
                case 2:
                    value.DataType = reader.ReadInt32();
                    break;
                case 4:
                    if (!sawFloats)
                    {
                        value.FloatDataPacked = wireType == ProtobufWireType.LengthDelimited;
                        sawFloats = true;
                    }

                    ReadFloatField(ref reader, wireType, value.FloatData);
                    break;
                case 5:
                    if (!sawInt32)
                    {
                        value.Int32DataPacked = wireType == ProtobufWireType.LengthDelimited;
                        sawInt32 = true;
                    }

                    ReadInt32Field(ref reader, wireType, value.Int32Data);
                    break;
                case 6:
                    value.StringData.Add(reader.ReadBytes());
                    break;
                case 7:
                    if (!sawInt64)
                    {
                        value.Int64DataPacked = wireType == ProtobufWireType.LengthDelimited;
                        sawInt64 = true;
                    }

                    ReadInt64Field(ref reader, wireType, value.Int64Data);
                    break;
                case 8:
                    value.Name = reader.ReadString();
                    break;
                case 9:
                    value.RawData = reader.ReadBytes();
                    break;
                case 10:
                    if (!sawDoubles)
                    {
                        value.DoubleDataPacked = wireType == ProtobufWireType.LengthDelimited;
                        sawDoubles = true;
                    }

                    ReadDoubleField(ref reader, wireType, value.DoubleData);
                    break;
                case 11:
                    if (!sawUInt64)
                    {
                        value.UInt64DataPacked = wireType == ProtobufWireType.LengthDelimited;
                        sawUInt64 = true;
                    }

                    ReadUInt64Field(ref reader, wireType, value.UInt64Data);
                    break;
                case 12:
                    value.DocString = reader.ReadString();
                    break;
                case 13:
                    value.ExternalData.Add(OnnxStringStringEntry.Parse(reader.ReadLengthDelimited()));
                    break;
                case 14:
                    value.DataLocation = reader.ReadInt32();
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

    /// <summary>Reads one of this tensor's side-file entries.</summary>
    /// <param name="key">The entry key, such as <c>location</c>, <c>offset</c> or <c>length</c>.</param>
    /// <returns>The entry's value, or <see langword="null"/> when the tensor has no such entry.</returns>
    internal string GetExternalDataValue(string key)
    {
        foreach (OnnxStringStringEntry entry in ExternalData)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <summary>Removes the side-file entries and marks the tensor as holding its own bytes.</summary>
    /// <remarks>
    /// The location is written out as an explicit zero rather than left absent, because that is what the ONNX package
    /// does when it reads a side file back into a model: it sets <c>data_location</c> to its default and clears the
    /// entries, and the field is one with explicit presence, so a model that has been through a side file and back
    /// carries a zero where a model that never left one carries nothing. A managed engine that left the field absent
    /// would write a different file from the one the ONNX tools write for the same model.
    /// </remarks>
    internal void ClearExternalData()
    {
        ExternalData.Clear();
        DataLocation = (int)OnnxDataLocation.Default;
    }

    /// <inheritdoc />
    public int CalculateSize()
    {
        int size = 0;
        size += MeasureInt64Field(1, Dimensions, DimensionsPacked, out _);
        if (DataType.HasValue)
        {
            size += ProtobufSizes.VarintField(2, DataType.Value);
        }

        size += MeasureFloatField(4, FloatData, FloatDataPacked, out _floatDataPayload);
        size += MeasureInt32Field(5, Int32Data, Int32DataPacked, out _int32DataPayload);
        foreach (byte[] text in StringData)
        {
            size += ProtobufSizes.BytesField(6, text.Length);
        }

        size += MeasureInt64Field(7, Int64Data, Int64DataPacked, out _);
        if (Name != null)
        {
            size += ProtobufSizes.StringField(8, Name);
        }

        if (RawData != null)
        {
            size += ProtobufSizes.BytesField(9, RawData.Length);
        }

        size += MeasureDoubleField(10, DoubleData, DoubleDataPacked, out _doubleDataPayload);
        size += MeasureUInt64Field(11, UInt64Data, UInt64DataPacked, out _uint64DataPayload);
        if (DocString != null)
        {
            size += ProtobufSizes.StringField(12, DocString);
        }

        foreach (OnnxStringStringEntry entry in ExternalData)
        {
            entry.CalculateSize();
            size += ProtobufSizes.MessageField(13, entry);
        }

        if (DataLocation.HasValue)
        {
            size += ProtobufSizes.VarintField(14, DataLocation.Value);
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
        if (Dimensions.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 1);
            WriteInt64Field(writer, 1, Dimensions, DimensionsPacked);
        }

        if (DataType.HasValue)
        {
            UnknownFields.WriteBefore(writer, 2);
            writer.WriteVarintField(2, DataType.Value);
        }

        if (FloatData.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 4);
            if (FloatDataPacked)
            {
                writer.WriteTag(4, ProtobufWireType.LengthDelimited);
                writer.WriteVarint((ulong)_floatDataPayload);
                foreach (float element in FloatData)
                {
                    writer.WriteFixed32(element);
                }
            }
            else
            {
                foreach (float element in FloatData)
                {
                    writer.WriteFloatField(4, element);
                }
            }
        }

        if (Int32Data.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 5);
            if (Int32DataPacked)
            {
                writer.WriteTag(5, ProtobufWireType.LengthDelimited);
                writer.WriteVarint((ulong)_int32DataPayload);
                foreach (int element in Int32Data)
                {
                    writer.WriteVarint((long)element);
                }
            }
            else
            {
                foreach (int element in Int32Data)
                {
                    writer.WriteVarintField(5, (long)element);
                }
            }
        }

        if (StringData.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 6);
            foreach (byte[] text in StringData)
            {
                writer.WriteBytesField(6, text);
            }
        }

        if (Int64Data.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 7);
            WriteInt64Field(writer, 7, Int64Data, Int64DataPacked);
        }

        if (Name != null)
        {
            UnknownFields.WriteBefore(writer, 8);
            writer.WriteStringField(8, Name);
        }

        if (RawData != null)
        {
            UnknownFields.WriteBefore(writer, 9);
            writer.WriteBytesField(9, RawData);
        }

        if (DoubleData.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 10);
            if (DoubleDataPacked)
            {
                writer.WriteTag(10, ProtobufWireType.LengthDelimited);
                writer.WriteVarint((ulong)_doubleDataPayload);
                foreach (double element in DoubleData)
                {
                    writer.WriteFixed64(element);
                }
            }
            else
            {
                foreach (double element in DoubleData)
                {
                    writer.WriteTag(10, ProtobufWireType.Fixed64);
                    writer.WriteFixed64(element);
                }
            }
        }

        if (UInt64Data.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 11);
            if (UInt64DataPacked)
            {
                writer.WriteTag(11, ProtobufWireType.LengthDelimited);
                writer.WriteVarint((ulong)_uint64DataPayload);
                foreach (ulong element in UInt64Data)
                {
                    writer.WriteVarint(element);
                }
            }
            else
            {
                foreach (ulong element in UInt64Data)
                {
                    writer.WriteTag(11, ProtobufWireType.Varint);
                    writer.WriteVarint(element);
                }
            }
        }

        if (DocString != null)
        {
            UnknownFields.WriteBefore(writer, 12);
            writer.WriteStringField(12, DocString);
        }

        if (ExternalData.Count > 0)
        {
            UnknownFields.WriteBefore(writer, 13);
            foreach (OnnxStringStringEntry entry in ExternalData)
            {
                writer.WriteMessageField(13, entry);
            }
        }

        if (DataLocation.HasValue)
        {
            UnknownFields.WriteBefore(writer, 14);
            writer.WriteVarintField(14, DataLocation.Value);
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

    private static void ReadInt64Field(ref ProtobufReader reader, ProtobufWireType wireType, List<long> target)
    {
        if (wireType != ProtobufWireType.LengthDelimited)
        {
            target.Add(reader.ReadInt64());
            return;
        }

        ProtobufReader inner = new ProtobufReader(reader.ReadLengthDelimited());
        while (!inner.IsAtEnd)
        {
            target.Add(inner.ReadInt64());
        }
    }

    private static void ReadInt32Field(ref ProtobufReader reader, ProtobufWireType wireType, List<int> target)
    {
        if (wireType != ProtobufWireType.LengthDelimited)
        {
            target.Add(reader.ReadInt32());
            return;
        }

        ProtobufReader inner = new ProtobufReader(reader.ReadLengthDelimited());
        while (!inner.IsAtEnd)
        {
            target.Add(inner.ReadInt32());
        }
    }

    private static void ReadUInt64Field(ref ProtobufReader reader, ProtobufWireType wireType, List<ulong> target)
    {
        if (wireType != ProtobufWireType.LengthDelimited)
        {
            target.Add(reader.ReadVarint());
            return;
        }

        ProtobufReader inner = new ProtobufReader(reader.ReadLengthDelimited());
        while (!inner.IsAtEnd)
        {
            target.Add(inner.ReadVarint());
        }
    }

    private static void ReadFloatField(ref ProtobufReader reader, ProtobufWireType wireType, List<float> target)
    {
        if (wireType != ProtobufWireType.LengthDelimited)
        {
            target.Add(reader.ReadFloat());
            return;
        }

        ProtobufReader inner = new ProtobufReader(reader.ReadLengthDelimited());
        while (!inner.IsAtEnd)
        {
            target.Add(inner.ReadFloat());
        }
    }

    private static void ReadDoubleField(ref ProtobufReader reader, ProtobufWireType wireType, List<double> target)
    {
        if (wireType != ProtobufWireType.LengthDelimited)
        {
            target.Add(reader.ReadDouble());
            return;
        }

        ProtobufReader inner = new ProtobufReader(reader.ReadLengthDelimited());
        while (!inner.IsAtEnd)
        {
            target.Add(inner.ReadDouble());
        }
    }

    private static int MeasureInt64Field(int fieldNumber, List<long> values, bool packed, out int payload)
    {
        payload = 0;
        if (values.Count == 0)
        {
            return 0;
        }

        if (!packed)
        {
            int tag = ProtobufSizes.Tag(fieldNumber);
            int size = 0;
            foreach (long value in values)
            {
                size += tag + ProtobufSizes.Varint(value);
            }

            return size;
        }

        foreach (long value in values)
        {
            payload += ProtobufSizes.Varint(value);
        }

        return ProtobufSizes.BytesField(fieldNumber, payload);
    }

    private static int MeasureInt32Field(int fieldNumber, List<int> values, bool packed, out int payload)
    {
        payload = 0;
        if (values.Count == 0)
        {
            return 0;
        }

        if (!packed)
        {
            int tag = ProtobufSizes.Tag(fieldNumber);
            int size = 0;
            foreach (int value in values)
            {
                size += tag + ProtobufSizes.Varint((long)value);
            }

            return size;
        }

        foreach (int value in values)
        {
            payload += ProtobufSizes.Varint((long)value);
        }

        return ProtobufSizes.BytesField(fieldNumber, payload);
    }

    private static int MeasureUInt64Field(int fieldNumber, List<ulong> values, bool packed, out int payload)
    {
        payload = 0;
        if (values.Count == 0)
        {
            return 0;
        }

        if (!packed)
        {
            int tag = ProtobufSizes.Tag(fieldNumber);
            int size = 0;
            foreach (ulong value in values)
            {
                size += tag + ProtobufSizes.Varint(value);
            }

            return size;
        }

        foreach (ulong value in values)
        {
            payload += ProtobufSizes.Varint(value);
        }

        return ProtobufSizes.BytesField(fieldNumber, payload);
    }

    private static int MeasureFloatField(int fieldNumber, List<float> values, bool packed, out int payload)
    {
        payload = values.Count * 4;
        if (values.Count == 0)
        {
            payload = 0;
            return 0;
        }

        return packed
            ? ProtobufSizes.BytesField(fieldNumber, payload)
            : values.Count * (ProtobufSizes.Tag(fieldNumber) + 4);
    }

    private static int MeasureDoubleField(int fieldNumber, List<double> values, bool packed, out int payload)
    {
        payload = values.Count * 8;
        if (values.Count == 0)
        {
            payload = 0;
            return 0;
        }

        return packed
            ? ProtobufSizes.BytesField(fieldNumber, payload)
            : values.Count * (ProtobufSizes.Tag(fieldNumber) + 8);
    }

    private static void WriteInt64Field(ProtobufWriter writer, int fieldNumber, List<long> values, bool packed)
    {
        if (!packed)
        {
            foreach (long value in values)
            {
                writer.WriteVarintField(fieldNumber, value);
            }

            return;
        }

        int payload = 0;
        foreach (long value in values)
        {
            payload += ProtobufSizes.Varint(value);
        }

        writer.WriteTag(fieldNumber, ProtobufWireType.LengthDelimited);
        writer.WriteVarint((ulong)payload);
        foreach (long value in values)
        {
            writer.WriteVarint(value);
        }
    }
}

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The ONNX <c>StringStringEntryProto</c> message: one key and one value, used for model metadata and for the
/// location, offset, length and checksum of a tensor held in a side file.
/// </summary>
internal sealed class OnnxStringStringEntry : IProtobufMessage
{
    private int _cachedSize;

    /// <summary>The entry's key, or <see langword="null"/> when the field is absent.</summary>
    internal string Key { get; set; }

    /// <summary>The entry's value, or <see langword="null"/> when the field is absent.</summary>
    internal string Value { get; set; }

    /// <summary>The fields of this message the codec does not model.</summary>
    internal ProtobufUnknownFields UnknownFields { get; } = new ProtobufUnknownFields();

    /// <inheritdoc />
    public int CachedSize => _cachedSize;

    /// <summary>Reads one entry from a message body.</summary>
    /// <param name="body">The encoded message body.</param>
    /// <returns>The parsed entry.</returns>
    internal static OnnxStringStringEntry Parse(System.ReadOnlySpan<byte> body)
    {
        OnnxStringStringEntry entry = new OnnxStringStringEntry();
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
                    entry.Key = reader.ReadString();
                    break;
                case 2:
                    entry.Value = reader.ReadString();
                    break;
                default:
                    reader.SkipValue(wireType);
                    entry.UnknownFields.Add(fieldNumber, reader.Slice(tagStart, reader.Position));
                    break;
            }
        }

        return entry;
    }

    /// <summary>Creates an entry with both fields present.</summary>
    /// <param name="key">The key to store.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The new entry.</returns>
    internal static OnnxStringStringEntry Create(string key, string value) =>
        new OnnxStringStringEntry { Key = key, Value = value };

    /// <inheritdoc />
    public int CalculateSize()
    {
        int size = 0;
        if (Key != null)
        {
            size += ProtobufSizes.StringField(1, Key);
        }

        if (Value != null)
        {
            size += ProtobufSizes.StringField(2, Value);
        }

        size += UnknownFields.TotalSize;
        _cachedSize = size;
        return size;
    }

    /// <inheritdoc />
    public void WriteTo(ProtobufWriter writer)
    {
        UnknownFields.BeginWrite();
        if (Key != null)
        {
            UnknownFields.WriteBefore(writer, 1);
            writer.WriteStringField(1, Key);
        }

        if (Value != null)
        {
            UnknownFields.WriteBefore(writer, 2);
            writer.WriteStringField(2, Value);
        }

        UnknownFields.WriteRemaining(writer);
    }
}

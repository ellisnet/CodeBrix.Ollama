using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama fs/gguf/gguf.go;

/// <summary>
/// The sequential GGUF header parser behind <see cref="GgufMetadata"/>. It reads the magic, the version, the
/// key-values and the tensor descriptors through one forward-only buffer, and never touches tensor data.
/// </summary>
internal sealed class GgufReader
{
    /// <summary>The longest string the reader accepts, matching Ollama's limit of 16 MB.</summary>
    internal const int MaxStringLength = 16 << 20;

    /// <summary>The largest array element count the reader accepts, matching Ollama's limit of 64 M items.</summary>
    internal const int MaxArrayElements = 64 << 20;

    /// <summary>The largest number of dimensions a GGUF tensor may declare.</summary>
    internal const int MaxTensorDimensions = 4;

    /// <summary>The alignment used when a file does not carry <c>general.alignment</c>.</summary>
    internal const long DefaultAlignment = 32;

    private const int BufferCapacity = 32 * 1024;
    private const int InitialListCapacity = 4096;

    private delegate T ElementDecoder<out T>(ReadOnlyMemory<byte> source, bool bigEndian);

    private static readonly ElementDecoder<byte> DecodeUInt8Element =
        static (source, bigEndian) => source.Span[0];

    private static readonly ElementDecoder<sbyte> DecodeInt8Element =
        static (source, bigEndian) => (sbyte)source.Span[0];

    private static readonly ElementDecoder<bool> DecodeBoolElement =
        static (source, bigEndian) => source.Span[0] != 0;

    private static readonly ElementDecoder<ushort> DecodeUInt16Element =
        static (source, bigEndian) => bigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(source.Span)
            : BinaryPrimitives.ReadUInt16LittleEndian(source.Span);

    private static readonly ElementDecoder<short> DecodeInt16Element =
        static (source, bigEndian) => bigEndian
            ? BinaryPrimitives.ReadInt16BigEndian(source.Span)
            : BinaryPrimitives.ReadInt16LittleEndian(source.Span);

    private static readonly ElementDecoder<uint> DecodeUInt32Element =
        static (source, bigEndian) => bigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(source.Span)
            : BinaryPrimitives.ReadUInt32LittleEndian(source.Span);

    private static readonly ElementDecoder<int> DecodeInt32Element =
        static (source, bigEndian) => bigEndian
            ? BinaryPrimitives.ReadInt32BigEndian(source.Span)
            : BinaryPrimitives.ReadInt32LittleEndian(source.Span);

    private static readonly ElementDecoder<ulong> DecodeUInt64Element =
        static (source, bigEndian) => bigEndian
            ? BinaryPrimitives.ReadUInt64BigEndian(source.Span)
            : BinaryPrimitives.ReadUInt64LittleEndian(source.Span);

    private static readonly ElementDecoder<long> DecodeInt64Element =
        static (source, bigEndian) => bigEndian
            ? BinaryPrimitives.ReadInt64BigEndian(source.Span)
            : BinaryPrimitives.ReadInt64LittleEndian(source.Span);

    private static readonly ElementDecoder<float> DecodeFloat32Element =
        static (source, bigEndian) => bigEndian
            ? BinaryPrimitives.ReadSingleBigEndian(source.Span)
            : BinaryPrimitives.ReadSingleLittleEndian(source.Span);

    private static readonly ElementDecoder<double> DecodeFloat64Element =
        static (source, bigEndian) => bigEndian
            ? BinaryPrimitives.ReadDoubleBigEndian(source.Span)
            : BinaryPrimitives.ReadDoubleLittleEndian(source.Span);

    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private readonly int _maxArraySize;
    private int _bufferStart;
    private int _bufferEnd;
    private long _offset;
    private bool _bigEndian;
    private uint _version;

    private GgufReader(Stream stream, int maxArraySize)
    {
        _stream = stream;
        _buffer = new byte[BufferCapacity];
        _maxArraySize = maxArraySize;
    }

    /// <summary>Reads one GGUF header from a stream positioned at its first byte.</summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The metadata.</returns>
    internal static async Task<GgufMetadata> ReadAsync(Stream stream, GgufReadOptions options,
        CancellationToken cancellationToken)
    {
        var effectiveOptions = options ?? new GgufReadOptions();
        long fileSize = stream.CanSeek ? stream.Length : -1L;
        var reader = new GgufReader(stream, effectiveOptions.MaxArraySize);
        try
        {
            return await reader.ParseAsync(effectiveOptions, fileSize, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException exception)
        {
            throw new GgufFormatException(
                "The GGUF file ends part way through its header; the file is truncated.", exception);
        }
    }

    private async Task<GgufMetadata> ParseAsync(GgufReadOptions options, long fileSize,
        CancellationToken cancellationToken)
    {
        var magic = await ReadBlockAsync(4, cancellationToken).ConfigureAwait(false);
        if (IsMagic(magic, 'G', 'G', 'U', 'F'))
        {
            _bigEndian = false;
        }
        else if (IsMagic(magic, 'F', 'U', 'G', 'G'))
        {
            _bigEndian = true;
        }
        else
        {
            throw new GgufFormatException(
                "The file does not start with the GGUF magic number; its first four bytes are " +
                DescribeMagic(magic) + ".");
        }

        _version = await ReadUInt32Async(cancellationToken).ConfigureAwait(false);
        if (_version < 1)
        {
            throw new GgufFormatException("Unsupported GGUF version " + _version + ".");
        }

        long tensorCount = await ReadCountAsync(cancellationToken).ConfigureAwait(false);
        long keyValueCount = await ReadCountAsync(cancellationToken).ConfigureAwait(false);

        var keyValues = new OrderedDictionary<string, GgufValue>(StringComparer.Ordinal);
        var keys = new List<string>();
        var omittedKeys = new List<string>();
        for (long i = 0; i < keyValueCount; i++)
        {
            string key = await ReadStringAsync(cancellationToken).ConfigureAwait(false);
            uint valueType = await ReadUInt32Async(cancellationToken).ConfigureAwait(false);
            GgufValue value = await ReadValueAsync(valueType, cancellationToken).ConfigureAwait(false);
            if (keyValues.ContainsKey(key))
            {
                keyValues[key] = value;
            }
            else
            {
                keyValues.Add(key, value);
                keys.Add(key);
            }

            if (value.IsOmittedArray)
            {
                omittedKeys.Add(key);
            }
        }

        var tensors = new List<GgufTensorInfo>();
        for (long i = 0; i < tensorCount; i++)
        {
            tensors.Add(await ReadTensorAsync(cancellationToken).ConfigureAwait(false));
        }

        long alignment = ResolveAlignment(keyValues);
        long headerEnd = _offset;
        long tensorDataOffset = headerEnd + (alignment - headerEnd % alignment) % alignment;

        ulong parameterCount = 0;
        ulong tensorDataSize = 0;
        for (int i = 0; i < tensors.Count; i++)
        {
            GgufTensorInfo tensor = tensors[i];
            long elementCount = tensor.ElementCount;
            if (elementCount < 0 || (ulong)elementCount > ulong.MaxValue - parameterCount)
            {
                throw new GgufFormatException("Unsupported GGUF: the model parameter count overflows.");
            }

            parameterCount += (ulong)elementCount;

            long byteCount = tensor.ByteCount;
            if (byteCount > 0)
            {
                if ((ulong)byteCount > ulong.MaxValue - tensorDataSize)
                {
                    throw new GgufFormatException("Unsupported GGUF: the total tensor data size overflows.");
                }

                tensorDataSize += (ulong)byteCount;
            }
        }

        if (options.ValidateTensorData && fileSize >= 0)
        {
            for (int i = 0; i < tensors.Count; i++)
            {
                ValidateTensorRange(tensors[i], tensorDataOffset, fileSize);
            }
        }

        return new GgufMetadata(_version, _bigEndian, alignment, tensorDataOffset, fileSize, keyValues, keys,
            tensors, omittedKeys, parameterCount, tensorDataSize);
    }

    private static void ValidateTensorRange(GgufTensorInfo tensor, long tensorDataOffset, long fileSize)
    {
        long numBytes = tensor.ByteCount;
        if (numBytes <= 0)
        {
            throw new GgufFormatException(
                "Unsupported GGUF: the size of tensor \"" + tensor.Name + "\" is zero or overflows.");
        }

        if (tensor.Offset > long.MaxValue)
        {
            throw new GgufFormatException(
                "Unsupported GGUF: the offset of tensor \"" + tensor.Name + "\" exceeds the maximum offset.");
        }

        long offset = tensorDataOffset + (long)tensor.Offset;
        if (offset < tensorDataOffset || offset > fileSize || numBytes > fileSize - offset)
        {
            throw new GgufFormatException(
                "Unsupported GGUF: the offset and size of tensor \"" + tensor.Name + "\" exceed the file size.");
        }
    }

    private static long ResolveAlignment(OrderedDictionary<string, GgufValue> keyValues)
    {
        if (!keyValues.TryGetValue("general.alignment", out GgufValue value) || value == null)
        {
            return DefaultAlignment;
        }

        if (value.TryGetUInt64(out ulong unsignedAlignment))
        {
            if (unsignedAlignment == 0 || unsignedAlignment > long.MaxValue)
            {
                throw new GgufFormatException("Unsupported GGUF alignment " + unsignedAlignment + ".");
            }

            return (long)unsignedAlignment;
        }

        if (value.TryGetInt64(out long signedAlignment) && signedAlignment > 0)
        {
            return signedAlignment;
        }

        throw new GgufFormatException("Unsupported GGUF alignment " + value + ".");
    }

    private async ValueTask<GgufTensorInfo> ReadTensorAsync(CancellationToken cancellationToken)
    {
        string name = await ReadStringAsync(cancellationToken).ConfigureAwait(false);
        uint dimensions = await ReadUInt32Async(cancellationToken).ConfigureAwait(false);
        if (dimensions > MaxTensorDimensions)
        {
            throw new GgufFormatException("Unsupported GGUF: tensor \"" + name + "\" declares " + dimensions +
                " dimensions, more than the maximum of " + MaxTensorDimensions + ".");
        }

        var shape = new ulong[dimensions];
        for (int i = 0; i < shape.Length; i++)
        {
            shape[i] = await ReadUInt64Async(cancellationToken).ConfigureAwait(false);
        }

        uint tensorType = await ReadUInt32Async(cancellationToken).ConfigureAwait(false);
        ulong offset = await ReadUInt64Async(cancellationToken).ConfigureAwait(false);

        var info = new GgufTensorInfo(name, offset, shape, (GgufTensorType)tensorType);
        if (info.ElementCount < 0)
        {
            throw new GgufFormatException(
                "Unsupported GGUF: the element count of tensor \"" + name + "\" overflows.");
        }

        return info;
    }

    private async ValueTask<GgufValue> ReadValueAsync(uint valueType, CancellationToken cancellationToken)
    {
        switch ((GgufValueType)valueType)
        {
            case GgufValueType.UInt8:
                return GgufValue.CreateScalar(GgufValueType.UInt8,
                    await ReadScalarAsync(1, DecodeUInt8Element, cancellationToken).ConfigureAwait(false));
            case GgufValueType.Int8:
                return GgufValue.CreateScalar(GgufValueType.Int8,
                    await ReadScalarAsync(1, DecodeInt8Element, cancellationToken).ConfigureAwait(false));
            case GgufValueType.UInt16:
                return GgufValue.CreateScalar(GgufValueType.UInt16,
                    await ReadScalarAsync(2, DecodeUInt16Element, cancellationToken).ConfigureAwait(false));
            case GgufValueType.Int16:
                return GgufValue.CreateScalar(GgufValueType.Int16,
                    await ReadScalarAsync(2, DecodeInt16Element, cancellationToken).ConfigureAwait(false));
            case GgufValueType.UInt32:
                return GgufValue.CreateScalar(GgufValueType.UInt32,
                    await ReadScalarAsync(4, DecodeUInt32Element, cancellationToken).ConfigureAwait(false));
            case GgufValueType.Int32:
                return GgufValue.CreateScalar(GgufValueType.Int32,
                    await ReadScalarAsync(4, DecodeInt32Element, cancellationToken).ConfigureAwait(false));
            case GgufValueType.UInt64:
                return GgufValue.CreateScalar(GgufValueType.UInt64,
                    await ReadScalarAsync(8, DecodeUInt64Element, cancellationToken).ConfigureAwait(false));
            case GgufValueType.Int64:
                return GgufValue.CreateScalar(GgufValueType.Int64,
                    await ReadScalarAsync(8, DecodeInt64Element, cancellationToken).ConfigureAwait(false));
            case GgufValueType.Float32:
                return GgufValue.CreateScalar(GgufValueType.Float32,
                    await ReadScalarAsync(4, DecodeFloat32Element, cancellationToken).ConfigureAwait(false));
            case GgufValueType.Float64:
                return GgufValue.CreateScalar(GgufValueType.Float64,
                    await ReadScalarAsync(8, DecodeFloat64Element, cancellationToken).ConfigureAwait(false));
            case GgufValueType.Bool:
                return GgufValue.CreateScalar(GgufValueType.Bool,
                    await ReadScalarAsync(1, DecodeBoolElement, cancellationToken).ConfigureAwait(false));
            case GgufValueType.String:
                return GgufValue.CreateScalar(GgufValueType.String,
                    await ReadStringAsync(cancellationToken).ConfigureAwait(false));
            case GgufValueType.Array:
                return await ReadArrayAsync(cancellationToken).ConfigureAwait(false);
            default:
                throw new GgufFormatException("Unsupported GGUF value type " + valueType + ".");
        }
    }

    private async ValueTask<GgufValue> ReadArrayAsync(CancellationToken cancellationToken)
    {
        uint rawElementType = await ReadUInt32Async(cancellationToken).ConfigureAwait(false);
        ulong rawCount = await ReadUInt64Async(cancellationToken).ConfigureAwait(false);
        int count = CheckedLength(rawCount, "array size", MaxArrayElements);
        var elementType = (GgufValueType)rawElementType;
        switch (elementType)
        {
            case GgufValueType.UInt8:
                return await ReadFixedArrayAsync(elementType, count, 1, DecodeUInt8Element, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.Int8:
                return await ReadFixedArrayAsync(elementType, count, 1, DecodeInt8Element, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.UInt16:
                return await ReadFixedArrayAsync(elementType, count, 2, DecodeUInt16Element, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.Int16:
                return await ReadFixedArrayAsync(elementType, count, 2, DecodeInt16Element, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.UInt32:
                return await ReadFixedArrayAsync(elementType, count, 4, DecodeUInt32Element, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.Int32:
                return await ReadFixedArrayAsync(elementType, count, 4, DecodeInt32Element, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.UInt64:
                return await ReadFixedArrayAsync(elementType, count, 8, DecodeUInt64Element, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.Int64:
                return await ReadFixedArrayAsync(elementType, count, 8, DecodeInt64Element, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.Float32:
                return await ReadFixedArrayAsync(elementType, count, 4, DecodeFloat32Element, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.Float64:
                return await ReadFixedArrayAsync(elementType, count, 8, DecodeFloat64Element, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.Bool:
                return await ReadFixedArrayAsync(elementType, count, 1, DecodeBoolElement, cancellationToken)
                    .ConfigureAwait(false);
            case GgufValueType.String:
                return await ReadStringArrayAsync(count, cancellationToken).ConfigureAwait(false);
            default:
                throw new GgufFormatException("Unsupported GGUF array element type " + rawElementType + ".");
        }
    }

    private async ValueTask<GgufValue> ReadFixedArrayAsync<T>(GgufValueType elementType, int count, int width,
        ElementDecoder<T> decode, CancellationToken cancellationToken)
    {
        if (_maxArraySize >= 0 && count > _maxArraySize)
        {
            await SkipAsync((long)count * width, cancellationToken).ConfigureAwait(false);
            return GgufValue.CreateOmittedArray(elementType, count);
        }

        var values = new List<T>(Math.Min(count, InitialListCapacity));
        int perChunk = Math.Max(1, BufferCapacity / width);
        int remaining = count;
        while (remaining > 0)
        {
            int take = Math.Min(remaining, perChunk);
            ReadOnlyMemory<byte> chunk = await ReadBlockAsync(take * width, cancellationToken)
                .ConfigureAwait(false);
            DecodeChunk(chunk, take, width, decode, _bigEndian, values);
            remaining -= take;
        }

        return GgufValue.CreateArray(elementType, count, values.ToArray());
    }

    private static void DecodeChunk<T>(ReadOnlyMemory<byte> chunk, int count, int width, ElementDecoder<T> decode,
        bool bigEndian, List<T> values)
    {
        for (int i = 0; i < count; i++)
        {
            values.Add(decode(chunk.Slice(i * width, width), bigEndian));
        }
    }

    private async ValueTask<GgufValue> ReadStringArrayAsync(int count, CancellationToken cancellationToken)
    {
        if (_maxArraySize >= 0 && count > _maxArraySize)
        {
            for (int i = 0; i < count; i++)
            {
                await SkipStringAsync(cancellationToken).ConfigureAwait(false);
            }

            return GgufValue.CreateOmittedArray(GgufValueType.String, count);
        }

        var values = new List<string>(Math.Min(count, InitialListCapacity));
        for (int i = 0; i < count; i++)
        {
            values.Add(await ReadStringAsync(cancellationToken).ConfigureAwait(false));
        }

        return GgufValue.CreateArray(GgufValueType.String, count, values.ToArray());
    }

    private async ValueTask<string> ReadStringAsync(CancellationToken cancellationToken)
    {
        ulong rawLength = await ReadUInt64Async(cancellationToken).ConfigureAwait(false);
        int length = CheckedLength(rawLength, "string length", MaxStringLength);
        bool legacy = _version == 1;
        if (legacy && length == 0)
        {
            throw new GgufFormatException("Unsupported GGUF: a version 1 string has zero length.");
        }

        if (length == 0)
        {
            return string.Empty;
        }

        if (length <= BufferCapacity)
        {
            ReadOnlyMemory<byte> block = await ReadBlockAsync(length, cancellationToken).ConfigureAwait(false);
            return DecodeString(block, legacy);
        }

        var bytes = new byte[length];
        await ReadExactAsync(bytes, cancellationToken).ConfigureAwait(false);
        return DecodeString(bytes, legacy);
    }

    private async ValueTask SkipStringAsync(CancellationToken cancellationToken)
    {
        ulong rawLength = await ReadUInt64Async(cancellationToken).ConfigureAwait(false);
        int length = CheckedLength(rawLength, "string length", MaxStringLength);
        if (_version == 1)
        {
            if (length == 0)
            {
                throw new GgufFormatException("Unsupported GGUF: a version 1 string has zero length.");
            }

            await SkipAsync(length - 1, cancellationToken).ConfigureAwait(false);
            byte terminator = await ReadScalarAsync(1, DecodeUInt8Element, cancellationToken).ConfigureAwait(false);
            if (terminator != 0)
            {
                throw new GgufFormatException("Unsupported GGUF: a version 1 string is not null terminated.");
            }

            return;
        }

        await SkipAsync(length, cancellationToken).ConfigureAwait(false);
    }

    private static string DecodeString(ReadOnlyMemory<byte> source, bool legacy)
    {
        if (legacy)
        {
            if (source.Span[source.Length - 1] != 0)
            {
                throw new GgufFormatException("Unsupported GGUF: a version 1 string is not null terminated.");
            }

            source = source.Slice(0, source.Length - 1);
        }

        return Encoding.UTF8.GetString(source.Span);
    }

    private async ValueTask<long> ReadCountAsync(CancellationToken cancellationToken)
    {
        ulong count = _version == 1
            ? await ReadScalarAsync(4, DecodeUInt32Element, cancellationToken).ConfigureAwait(false)
            : await ReadUInt64Async(cancellationToken).ConfigureAwait(false);
        if (count > int.MaxValue)
        {
            throw new GgufFormatException(
                "A GGUF item count of " + count + " exceeds the maximum of " + int.MaxValue + ".");
        }

        return (long)count;
    }

    private ValueTask<uint> ReadUInt32Async(CancellationToken cancellationToken)
    {
        return ReadScalarAsync(4, DecodeUInt32Element, cancellationToken);
    }

    private ValueTask<ulong> ReadUInt64Async(CancellationToken cancellationToken)
    {
        return ReadScalarAsync(8, DecodeUInt64Element, cancellationToken);
    }

    private async ValueTask<T> ReadScalarAsync<T>(int width, ElementDecoder<T> decode,
        CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> block = await ReadBlockAsync(width, cancellationToken).ConfigureAwait(false);
        return decode(block, _bigEndian);
    }

    private async ValueTask<ReadOnlyMemory<byte>> ReadBlockAsync(int count, CancellationToken cancellationToken)
    {
        if (_bufferEnd - _bufferStart < count)
        {
            await FillAsync(count, cancellationToken).ConfigureAwait(false);
        }

        var block = new ReadOnlyMemory<byte>(_buffer, _bufferStart, count);
        _bufferStart += count;
        _offset += count;
        return block;
    }

    private async ValueTask ReadExactAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        int available = _bufferEnd - _bufferStart;
        if (available > 0)
        {
            int take = Math.Min(available, destination.Length);
            new ReadOnlyMemory<byte>(_buffer, _bufferStart, take).CopyTo(destination);
            _bufferStart += take;
            _offset += take;
            destination = destination.Slice(take);
        }

        while (destination.Length > 0)
        {
            int read = await _stream.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            _offset += read;
            destination = destination.Slice(read);
        }
    }

    private async ValueTask SkipAsync(long count, CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return;
        }

        int available = _bufferEnd - _bufferStart;
        if (available > 0)
        {
            int take = (int)Math.Min(available, count);
            _bufferStart += take;
            _offset += take;
            count -= take;
        }

        if (count == 0)
        {
            return;
        }

        _bufferStart = 0;
        _bufferEnd = 0;
        while (count > 0)
        {
            int take = (int)Math.Min(count, BufferCapacity);
            int read = await _stream.ReadAsync(new Memory<byte>(_buffer, 0, take), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            _offset += read;
            count -= read;
        }

        _bufferStart = 0;
        _bufferEnd = 0;
    }

    private async ValueTask FillAsync(int minimum, CancellationToken cancellationToken)
    {
        int available = _bufferEnd - _bufferStart;
        if (_bufferStart > 0)
        {
            if (available > 0)
            {
                Buffer.BlockCopy(_buffer, _bufferStart, _buffer, 0, available);
            }

            _bufferStart = 0;
            _bufferEnd = available;
        }

        while (_bufferEnd < minimum)
        {
            int read = await _stream.ReadAsync(new Memory<byte>(_buffer, _bufferEnd, _buffer.Length - _bufferEnd),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            _bufferEnd += read;
        }
    }

    private static int CheckedLength(ulong value, string kind, int maximum)
    {
        if (value > int.MaxValue)
        {
            throw new GgufFormatException(
                "A GGUF " + kind + " of " + value + " exceeds the maximum of " + int.MaxValue + ".");
        }

        if (value > (ulong)maximum)
        {
            throw new GgufFormatException(
                "A GGUF " + kind + " of " + value + " exceeds the maximum of " + maximum + ".");
        }

        return (int)value;
    }

    private static bool IsMagic(ReadOnlyMemory<byte> magic, char a, char b, char c, char d)
    {
        ReadOnlySpan<byte> span = magic.Span;
        return span[0] == (byte)a && span[1] == (byte)b && span[2] == (byte)c && span[3] == (byte)d;
    }

    private static string DescribeMagic(ReadOnlyMemory<byte> magic)
    {
        ReadOnlySpan<byte> span = magic.Span;
        var builder = new StringBuilder(11);
        for (int i = 0; i < span.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(span[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}

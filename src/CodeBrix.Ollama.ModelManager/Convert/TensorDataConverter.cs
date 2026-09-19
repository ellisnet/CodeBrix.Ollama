using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: gguf-py/gguf/quants.py@b10221, conversion/llama.py@b10221;

/// <summary>
/// Moves one tensor's values from a checkpoint into a GGUF file: widening or narrowing the numbers, and
/// applying the rotary permutation to the query and key projections on the way.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic is the inference engine converter's, to the bit. Every source type is widened to a 32-bit
/// float first, exactly as the converter widens anything that is not already 16- or 32-bit float; a 16-bit
/// float result is the nearest one, ties to even, with values too large becoming infinity; and a bfloat16
/// result is the top sixteen bits of the 32-bit float, rounded to nearest with ties to even, with a signalling
/// not-a-number made quiet first.
/// </para>
/// <para>
/// A tensor that is not permuted is converted a block at a time and never held whole. A permuted one has to be
/// held, because the permutation moves rows about - but the only permuted tensors are the query and key
/// projections, which are small even in a model of several billion parameters.
/// </para>
/// </remarks>
internal static class TensorDataConverter
{
    private const int BlockElements = 1 << 16;

    /// <summary>Converts one tensor's values and writes them.</summary>
    /// <param name="source">The checkpoint's bytes for this tensor.</param>
    /// <param name="destination">The GGUF file being written.</param>
    /// <param name="sourceType">The element type the checkpoint holds.</param>
    /// <param name="targetType">The ggml type to write.</param>
    /// <param name="elementCount">How many values there are.</param>
    /// <param name="permuteHeadCount">The head count to permute by, or 0 for no permutation.</param>
    /// <param name="rowCount">The size of the tensor's outermost dimension.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A task that completes when the values have been written.</returns>
    internal static async Task ConvertAsync(Stream source, Stream destination, CheckpointDataType sourceType,
        GgufTensorType targetType, long elementCount, long permuteHeadCount, long rowCount,
        CancellationToken cancellationToken)
    {
        if (permuteHeadCount > 0)
        {
            await ConvertPermutedAsync(source, destination, sourceType, targetType, elementCount,
                permuteHeadCount, rowCount, cancellationToken).ConfigureAwait(false);
            return;
        }

        int sourceWidth = CheckpointDataTypes.GetByteWidth(sourceType);
        var sourceBytes = new byte[BlockElements * sourceWidth];
        var values = new float[BlockElements];
        long remaining = elementCount;
        while (remaining > 0)
        {
            int take = (int)Math.Min(remaining, BlockElements);
            await source.ReadExactlyAsync(new Memory<byte>(sourceBytes, 0, take * sourceWidth),
                cancellationToken).ConfigureAwait(false);
            Decode(new ReadOnlySpan<byte>(sourceBytes, 0, take * sourceWidth), sourceType,
                new Span<float>(values, 0, take));
            await WriteAsync(destination, targetType, new ReadOnlyMemory<float>(values, 0, take),
                cancellationToken).ConfigureAwait(false);
            remaining -= take;
        }
    }

    /// <summary>Widens a block of checkpoint bytes into 32-bit floats.</summary>
    /// <param name="source">The bytes.</param>
    /// <param name="sourceType">The element type they hold.</param>
    /// <param name="destination">Receives one value per element.</param>
    internal static void Decode(ReadOnlySpan<byte> source, CheckpointDataType sourceType, Span<float> destination)
    {
        switch (sourceType)
        {
            case CheckpointDataType.F32:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = BinaryPrimitives.ReadSingleLittleEndian(source.Slice(i * 4, 4));
                }

                return;
            case CheckpointDataType.F16:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = (float)BinaryPrimitives.ReadHalfLittleEndian(source.Slice(i * 2, 2));
                }

                return;
            case CheckpointDataType.BF16:
                for (int i = 0; i < destination.Length; i++)
                {
                    uint bits = (uint)BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(i * 2, 2)) << 16;
                    destination[i] = BitConverter.UInt32BitsToSingle(bits);
                }

                return;
            case CheckpointDataType.F64:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = (float)BinaryPrimitives.ReadDoubleLittleEndian(source.Slice(i * 8, 8));
                }

                return;
            case CheckpointDataType.I64:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(i * 8, 8));
                }

                return;
            case CheckpointDataType.I32:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(i * 4, 4));
                }

                return;
            case CheckpointDataType.I16:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = BinaryPrimitives.ReadInt16LittleEndian(source.Slice(i * 2, 2));
                }

                return;
            case CheckpointDataType.I8:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = (sbyte)source[i];
                }

                return;
            default:
                for (int i = 0; i < destination.Length; i++)
                {
                    destination[i] = source[i];
                }

                return;
        }
    }

    /// <summary>Narrows a 32-bit float to bfloat16, the way the engine's own conversion does.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The sixteen bits to write.</returns>
    internal static ushort ToBFloat16(float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        if ((bits & 0x7fffffffu) > 0x7f800000u)
        {
            // A not-a-number is made quiet before it is narrowed, so the result is still a not-a-number.
            bits = (bits & 0xffff0000u) | (64u << 16);
        }

        ulong rounded = (ulong)bits + 0x7fffUL + ((bits >> 16) & 1UL);
        return (ushort)(rounded >> 16);
    }

    private static async Task ConvertPermutedAsync(Stream source, Stream destination,
        CheckpointDataType sourceType, GgufTensorType targetType, long elementCount, long permuteHeadCount,
        long rowCount, CancellationToken cancellationToken)
    {
        if (rowCount <= 0 || permuteHeadCount <= 0 || rowCount % (2 * permuteHeadCount) != 0)
        {
            throw new CheckpointFormatException("A tensor with " + rowCount + " rows cannot be laid out for " +
                permuteHeadCount + " attention heads: the rotary permutation needs the rows to divide into " +
                "two halves per head.");
        }

        long rowElements = elementCount / rowCount;
        if (rowElements * rowCount != elementCount)
        {
            throw new CheckpointFormatException(
                "A tensor of " + elementCount + " values does not divide into " + rowCount + " rows.");
        }

        if (elementCount > int.MaxValue)
        {
            throw new CheckpointFormatException("A tensor of " + elementCount + " values is too large for the " +
                "rotary permutation, which has to hold the tensor while it moves its rows.");
        }

        var values = new float[elementCount];
        int sourceWidth = CheckpointDataTypes.GetByteWidth(sourceType);
        var sourceBytes = new byte[BlockElements * sourceWidth];
        long read = 0;
        while (read < elementCount)
        {
            int take = (int)Math.Min(elementCount - read, BlockElements);
            await source.ReadExactlyAsync(new Memory<byte>(sourceBytes, 0, take * sourceWidth),
                cancellationToken).ConfigureAwait(false);
            Decode(new ReadOnlySpan<byte>(sourceBytes, 0, take * sourceWidth), sourceType,
                new Span<float>(values, (int)read, take));
            read += take;
        }

        long rowsPerHead = rowCount / permuteHeadCount;
        long halfRowsPerHead = rowsPerHead / 2;
        var row = new float[rowElements];
        for (long head = 0; head < permuteHeadCount; head++)
        {
            for (long index = 0; index < halfRowsPerHead; index++)
            {
                for (long half = 0; half < 2; half++)
                {
                    long sourceRow = head * rowsPerHead + half * halfRowsPerHead + index;
                    Array.Copy(values, sourceRow * rowElements, row, 0, rowElements);
                    await WriteAsync(destination, targetType, row, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task WriteAsync(Stream destination, GgufTensorType targetType,
        ReadOnlyMemory<float> values, CancellationToken cancellationToken)
    {
        int width = targetType == GgufTensorType.F32 ? 4 : 2;
        var bytes = new byte[values.Length * width];
        Encode(values.Span, targetType, bytes);
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void Encode(ReadOnlySpan<float> values, GgufTensorType targetType, Span<byte> destination)
    {
        switch (targetType)
        {
            case GgufTensorType.F32:
                for (int i = 0; i < values.Length; i++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(i * 4, 4), values[i]);
                }

                return;
            case GgufTensorType.F16:
                for (int i = 0; i < values.Length; i++)
                {
                    BinaryPrimitives.WriteHalfLittleEndian(destination.Slice(i * 2, 2), (Half)values[i]);
                }

                return;
            case GgufTensorType.BF16:
                for (int i = 0; i < values.Length; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(i * 2, 2), ToBFloat16(values[i]));
                }

                return;
            default:
                throw new NotSupportedException(
                    "A GGUF tensor of type " + targetType + " is not one this converter writes.");
        }
    }
}

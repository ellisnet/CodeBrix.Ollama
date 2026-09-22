using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The block-quantized matrix multiplication: a float left-hand side against a weight that is still packed,
/// with each block's values turned back into floats INSIDE the loop that multiplies them.
/// </summary>
/// <remarks>
/// <para>
/// THE UNPACK HAS TO BE VECTORISED. Taking four-bit values out of bytes one at a time is about ten times
/// slower than taking eight of them out of one 32-bit load, and that ratio decides whether a quantized model
/// is faster than the full-precision one it came from or several times slower. The wide path broadcasts the
/// four bytes that hold eight values, shifts each lane down by a different amount, masks off four bits, turns
/// the eight integers into floats and multiplies-and-adds them in one go. The scalar path does the same
/// arithmetic one value at a time and the suite holds the two to each other.
/// </para>
/// <para>
/// A COLUMN IS UNPACKED ONCE PER CALL, NOT ONCE PER ROW. Generating a token multiplies by one row and the
/// unpack is paid once either way; evaluating a PROMPT multiplies by as many rows as the prompt has tokens,
/// and unpacking the same column again for each of them made the four-bit path about twice as slow as the
/// full-precision one it replaced - a quantized model that reached its first token later than the model it
/// came from. With more than one row the column is turned back into floats once into a buffer of its own and
/// every row reads that, which leaves the unpack where it belongs: paid once per column.
/// </para>
/// <para>
/// IT IS THE SAME ARITHMETIC IN THE SAME ORDER. Each output element accumulates exactly the products it
/// accumulated before, in the same order, into the same shape of accumulator, so the two paths agree bit for
/// bit and not merely to within reassociation - the suite holds spreading over threads to that standard, and
/// so does this.
/// </para>
/// <para>
/// THE DEQUANTIZATION IS FUSED AND NOT MATERIALIZED (plan decision D3): the weight is never written out as
/// floats, not even a row of it, so a model reduced to a quarter of its size stays a quarter of its size while
/// it runs. What is materialized is one column at a time - a few thousand floats, which is smaller than this
/// processor's second-level cache - and only while that column is being multiplied.
/// </para>
/// <para>
/// The value of <c>accuracy_level</c> does not reach here. It asks a runtime to quantize the ACTIVATIONS as
/// well - level 4 means "you may use 8-bit activations" - and this engine keeps them in 32-bit floats, which
/// is the most accurate thing the attribute permits and is well inside the tolerance the plan sets for a
/// quantized graph.
/// </para>
/// </remarks>
internal static class OnnxBlockGemm
{
    /// <summary>
    /// How many rows a call must have before a column is turned back into floats ONCE for all of them rather
    /// than read straight into each row's multiply.
    /// </summary>
    /// <remarks>
    /// Writing the column out and reading it back costs memory traffic the fused loops do not pay, so with
    /// only a row or two it is a loss and with a prompt's worth of rows it is a large win. Measured on this
    /// engine's own subjects at a four-bit weight of 1024 by 1024: two rows are faster fused, eight rows are
    /// faster amortised, and four is where they meet.
    /// </remarks>
    internal const int AmortiseRows = 4;

    private static readonly Vector256<uint> NibbleShifts = Vector256.Create(0u, 4u, 8u, 12u, 16u, 20u, 24u, 28u);

    /// <summary>
    /// C[m, n] = sum over k of A[m, k] times the dequantized weight value at [n, k], plus the bias.
    /// </summary>
    /// <param name="a">The left-hand elements, [m, k] row-major.</param>
    /// <param name="weight">The packed weight.</param>
    /// <param name="c">Where the result goes, [m, n] row-major.</param>
    /// <param name="m">The number of left-hand rows.</param>
    /// <param name="kind">Which arithmetic path to take.</param>
    /// <param name="threads">How many threads to spread the work over.</param>
    internal static void Multiply(
        float[] a, OnnxBlockQuantizedWeight weight, float[] c, int m, OnnxKernelKind kind, int threads)
    {
        int n = weight.Width;
        long total = (long)m * n;
        if (total == 0) return;

        // The work is split by COLUMN rather than by output element, because a column is what the unpack is
        // paid for: a worker that owns a column owns every row of it and unpacks it once.
        long work = total * Math.Max(weight.Reduction, 1);
        int workers = work >= OnnxGemm.ParallelThreshold ? Math.Min(threads, n) : 1;

        if (workers <= 1)
        {
            Columns(a, weight, c, m, 0, n, kind);
            return;
        }

        int chunk = (n + workers - 1) / workers;
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
        {
            int start = worker * chunk;
            int end = Math.Min(n, start + chunk);
            if (start < end) Columns(a, weight, c, m, start, end, kind);
        });
    }

    /// <summary>The zero point one block of one column is measured from.</summary>
    /// <param name="weight">The packed weight.</param>
    /// <param name="column">Which column.</param>
    /// <param name="block">Which block of the reduction.</param>
    /// <returns>The zero point.</returns>
    internal static float ZeroPoint(OnnxBlockQuantizedWeight weight, int column, int block)
    {
        if (weight.FloatZeroPoints != null)
        {
            return weight.FloatZeroPoints[(column * weight.BlockCount) + block];
        }

        if (weight.PackedZeroPoints == null) return weight.DefaultZeroPoint;

        int offset = column * weight.ZeroPointBlobSize;
        if (weight.Bits == 8) return weight.PackedZeroPoints[offset + block];

        byte pair = weight.PackedZeroPoints[offset + (block / 2)];
        return (block & 1) == 0 ? pair & 0x0F : pair >> 4;
    }

    /// <summary>
    /// How much of a reduction the wide path multiplies eight at a time, which is everything except whatever
    /// the LAST block leaves over. Every block but the last is a whole power of two of at least sixteen, so
    /// nothing in the middle of a column is ever left over.
    /// </summary>
    /// <param name="weight">The packed weight.</param>
    /// <returns>The number of leading elements the eight-wide loop covers.</returns>
    internal static int WideLength(OnnxBlockQuantizedWeight weight)
    {
        int k = weight.Reduction;
        int over = weight.BlockSize <= 0 ? 0 : k % weight.BlockSize;
        return over == 0 ? k : k - (over & 7);
    }

    private static void Columns(
        float[] a, OnnxBlockQuantizedWeight weight, float[] c, int m, int firstColumn, int lastColumn,
        OnnxKernelKind kind)
    {
        int n = weight.Width;
        int k = weight.Reduction;
        bool wide = kind == OnnxKernelKind.Avx2 && Avx2.IsSupported && Fma.IsSupported;

        if (m < AmortiseRows)
        {
            // One row is a generated token and there is nothing to amortise the unpack over; two or three
            // rows would not pay for writing a column out and reading it back. The column is read straight
            // into the multiply, which is also what keeps a four-bit weight four bits wide.
            float[] block = wide ? null : new float[weight.BlockSize];
            int first = firstColumn;
            if (wide && (k & 7) == 0)
            {
                for (; first + 4 <= lastColumn; first += 4)
                {
                    for (int row = 0; row < m; row++)
                    {
                        if (weight.Bits == 8)
                            FourColumns8Wide(a, row * k, weight, c, row * n + first, first);
                        else
                            FourColumnsWide(a, row * k, weight, c, row * n + first, first);
                    }
                }
            }

            for (int j = first; j < lastColumn; j++)
            {
                for (int row = 0; row < m; row++)
                {
                    float sum = wide
                        ? DotWide(a, row * k, weight, j)
                        : DotNarrow(a, row * k, weight, j, block, kind);
                    if (weight.Bias != null) sum += weight.Bias[j];
                    c[(row * n) + j] = sum;
                }
            }

            return;
        }

        float[] column = new float[k];
        int aligned = WideLength(weight);
        for (int j = firstColumn; j < lastColumn; j++)
        {
            Dequantize(weight, j, column, wide);
            float bias = weight.Bias == null ? 0f : weight.Bias[j];
            for (int row = 0; row < m; row++)
            {
                float sum = wide
                    ? DotColumnWide(a, row * k, column, k, aligned)
                    : DotColumnNarrow(a, row * k, column, weight, kind);
                c[(row * n) + j] = sum + bias;
            }
        }
    }

    /// <summary>
    /// Turns one whole column of the weight back into floats, which is the same arithmetic the fused loops do
    /// and gives the same values: an integer taken from its block, less that block's zero point, times that
    /// block's scale.
    /// </summary>
    private static void Dequantize(
        OnnxBlockQuantizedWeight weight, int column, float[] target, bool wide)
    {
        int k = weight.Reduction;
        int blockSize = weight.BlockSize;
        int blobSize = weight.BlobSize;
        byte[] packed = weight.Packed;
        int blobBase = column * weight.BlockCount * blobSize;
        int scaleBase = column * weight.BlockCount;
        bool eightBit = weight.Bits == 8;

        ref byte pb = ref MemoryMarshal.GetArrayDataReference(packed);
        ref float pt = ref MemoryMarshal.GetArrayDataReference(target);
        Vector256<uint> mask = Vector256.Create(0x0000000Fu);

        for (int block = 0, k0 = 0; k0 < k; block++, k0 += blockSize)
        {
            int length = Math.Min(blockSize, k - k0);
            float scaleValue = weight.Scales[scaleBase + block];
            float zeroPointValue = ZeroPoint(weight, column, block);
            int blob = blobBase + (block * blobSize);

            int i = 0;
            if (wide)
            {
                Vector256<float> scale = Vector256.Create(scaleValue);
                Vector256<float> zeroPoint = Vector256.Create(zeroPointValue);
                for (; i + 8 <= length; i += 8)
                {
                    // EIGHT BYTES ARE READ, NOT SIXTEEN. A 128-bit load would walk up to eight bytes past the
                    // end of the weight on its very last block; reading the eight this needs as one 64-bit
                    // value and widening them cannot.
                    Vector256<float> quantized = eightBit
                        ? Avx.ConvertToVector256Single(
                            Avx2.ConvertToVector256Int32(
                                Vector128.CreateScalarUnsafe(
                                    Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref pb, (nuint)(blob + i))))
                                    .AsByte()))
                        : Avx.ConvertToVector256Single(
                            Avx2.And(
                                Avx2.ShiftRightLogicalVariable(
                                    Vector256.Create(
                                        Unsafe.ReadUnaligned<uint>(
                                            ref Unsafe.Add(ref pb, (nuint)(blob + (i >> 1))))),
                                    NibbleShifts),
                                mask).AsInt32());

                    ((quantized - zeroPoint) * scale).StoreUnsafe(ref pt, (nuint)(k0 + i));
                }
            }

            for (; i < length; i++)
            {
                int value = eightBit
                    ? packed[blob + i]
                    : ((i & 1) == 0
                        ? packed[blob + (i >> 1)] & 0x0F
                        : packed[blob + (i >> 1)] >> 4);
                target[k0 + i] = (value - zeroPointValue) * scaleValue;
            }
        }
    }

    private static float DotColumnWide(float[] a, int aOffset, float[] column, int k, int aligned)
    {
        ref float pa = ref MemoryMarshal.GetArrayDataReference(a);
        ref float pw = ref MemoryMarshal.GetArrayDataReference(column);
        Vector256<float> accumulated = Vector256<float>.Zero;

        int i = 0;
        for (; i < aligned; i += 8)
        {
            accumulated = Fma.MultiplyAdd(
                Vector256.LoadUnsafe(ref pa, (nuint)(aOffset + i)),
                Vector256.LoadUnsafe(ref pw, (nuint)i),
                accumulated);
        }

        float tail = 0f;
        for (; i < k; i++) tail += a[aOffset + i] * column[i];
        return Vector256.Sum(accumulated) + tail;
    }

    private static float DotColumnNarrow(
        float[] a, int aOffset, float[] column, OnnxBlockQuantizedWeight weight, OnnxKernelKind kind)
    {
        int k = weight.Reduction;
        int blockSize = weight.BlockSize;
        float sum = 0f;

        for (int k0 = 0; k0 < k; k0 += blockSize)
        {
            int length = Math.Min(blockSize, k - k0);
            sum += kind == OnnxKernelKind.Vector
                ? VectorDot(a, aOffset + k0, column, k0, length)
                : ScalarDot(a, aOffset + k0, column, k0, length);
        }

        return sum;
    }

    private static float DotNarrow(
        float[] a, int aOffset, OnnxBlockQuantizedWeight weight, int column, float[] scratch,
        OnnxKernelKind kind)
    {
        int k = weight.Reduction;
        int blockSize = weight.BlockSize;
        int blobSize = weight.BlobSize;
        byte[] packed = weight.Packed;
        int blobBase = column * weight.BlockCount * blobSize;
        int scaleBase = column * weight.BlockCount;
        float sum = 0f;

        for (int block = 0, k0 = 0; k0 < k; block++, k0 += blockSize)
        {
            int length = Math.Min(blockSize, k - k0);
            float scale = weight.Scales[scaleBase + block];
            float zeroPoint = ZeroPoint(weight, column, block);
            int blob = blobBase + (block * blobSize);

            if (weight.Bits == 8)
            {
                for (int i = 0; i < length; i++) scratch[i] = (packed[blob + i] - zeroPoint) * scale;
            }
            else
            {
                for (int i = 0; i < length; i++)
                {
                    byte pair = packed[blob + (i >> 1)];
                    int value = (i & 1) == 0 ? pair & 0x0F : pair >> 4;
                    scratch[i] = (value - zeroPoint) * scale;
                }
            }

            sum += kind == OnnxKernelKind.Vector
                ? VectorDot(a, aOffset + k0, scratch, 0, length)
                : ScalarDot(a, aOffset + k0, scratch, 0, length);
        }

        return sum;
    }

    private static float ScalarDot(float[] a, int aOffset, float[] values, int valuesOffset, int length)
    {
        float sum = 0f;
        for (int i = 0; i < length; i++) sum += a[aOffset + i] * values[valuesOffset + i];
        return sum;
    }

    private static float VectorDot(float[] a, int aOffset, float[] values, int valuesOffset, int length)
    {
        int width = Vector<float>.Count;
        Vector<float> accumulated = Vector<float>.Zero;
        int i = 0;
        for (; i + width <= length; i += width)
        {
            accumulated += new Vector<float>(a, aOffset + i) * new Vector<float>(values, valuesOffset + i);
        }

        float sum = Vector.Sum(accumulated);
        for (; i < length; i++) sum += a[aOffset + i] * values[valuesOffset + i];
        return sum;
    }

    /// <summary>
    /// Four output columns share each activation load and keep four independent accumulation chains.
    /// Every column still sums in the same order as DotWide; no weight expansion or activation quantization.
    /// </summary>
    private static void FourColumnsWide(
        float[] a, int aOffset, OnnxBlockQuantizedWeight weight, float[] c, int target, int column)
    {
        ref float pa = ref MemoryMarshal.GetArrayDataReference(a);
        ref byte pb = ref MemoryMarshal.GetArrayDataReference(weight.Packed);
        int blocks = weight.BlockCount;
        int blockSize = weight.BlockSize;
        int blobSize = weight.BlobSize;
        int columnBytes = blocks * blobSize;
        int firstBlob = column * columnBytes;
        int firstScale = column * blocks;
        Vector256<uint> mask = Vector256.Create(15u);
        Vector256<float> sum0 = Vector256<float>.Zero;
        Vector256<float> sum1 = Vector256<float>.Zero;
        Vector256<float> sum2 = Vector256<float>.Zero;
        Vector256<float> sum3 = Vector256<float>.Zero;

        for (int block = 0, k0 = 0; k0 < weight.Reduction; block++, k0 += blockSize)
        {
            int end = Math.Min(blockSize, weight.Reduction - k0);
            int blob = firstBlob + block * blobSize;
            Vector256<float> scale0 = Vector256.Create(weight.Scales[firstScale + block]);
            Vector256<float> zero0 = Vector256.Create(ZeroPoint(weight, column, block));
            Vector256<float> scale1 = Vector256.Create(weight.Scales[firstScale + blocks + block]);
            Vector256<float> zero1 = Vector256.Create(ZeroPoint(weight, column + 1, block));
            Vector256<float> scale2 = Vector256.Create(weight.Scales[firstScale + 2 * blocks + block]);
            Vector256<float> zero2 = Vector256.Create(ZeroPoint(weight, column + 2, block));
            Vector256<float> scale3 = Vector256.Create(weight.Scales[firstScale + 3 * blocks + block]);
            Vector256<float> zero3 = Vector256.Create(ZeroPoint(weight, column + 3, block));
            for (int i = 0; i < end; i += 8)
            {
                Vector256<float> left = Vector256.LoadUnsafe(ref pa, (nuint)(aOffset + k0 + i));
                int at = blob + (i >> 1);
                Vector256<float> q0 = Avx.ConvertToVector256Single(
                    Avx2.And(Avx2.ShiftRightLogicalVariable(
                        Vector256.Create(Unsafe.ReadUnaligned<uint>(
                            ref Unsafe.Add(ref pb, at))), NibbleShifts), mask).AsInt32());
                sum0 = Fma.MultiplyAdd(left, (q0 - zero0) * scale0, sum0);
                Vector256<float> q1 = Avx.ConvertToVector256Single(
                    Avx2.And(Avx2.ShiftRightLogicalVariable(
                        Vector256.Create(Unsafe.ReadUnaligned<uint>(
                            ref Unsafe.Add(ref pb, at + columnBytes))), NibbleShifts), mask).AsInt32());
                sum1 = Fma.MultiplyAdd(left, (q1 - zero1) * scale1, sum1);
                Vector256<float> q2 = Avx.ConvertToVector256Single(
                    Avx2.And(Avx2.ShiftRightLogicalVariable(
                        Vector256.Create(Unsafe.ReadUnaligned<uint>(
                            ref Unsafe.Add(ref pb, at + 2 * columnBytes))), NibbleShifts), mask).AsInt32());
                sum2 = Fma.MultiplyAdd(left, (q2 - zero2) * scale2, sum2);
                Vector256<float> q3 = Avx.ConvertToVector256Single(
                    Avx2.And(Avx2.ShiftRightLogicalVariable(
                        Vector256.Create(Unsafe.ReadUnaligned<uint>(
                            ref Unsafe.Add(ref pb, at + 3 * columnBytes))), NibbleShifts), mask).AsInt32());
                sum3 = Fma.MultiplyAdd(left, (q3 - zero3) * scale3, sum3);
            }
        }

        c[target] = Vector256.Sum(sum0) + (weight.Bias == null ? 0f : weight.Bias[column]);
        c[target + 1] = Vector256.Sum(sum1) + (weight.Bias == null ? 0f : weight.Bias[column + 1]);
        c[target + 2] = Vector256.Sum(sum2) + (weight.Bias == null ? 0f : weight.Bias[column + 2]);
        c[target + 3] = Vector256.Sum(sum3) + (weight.Bias == null ? 0f : weight.Bias[column + 3]);
    }

    /// <summary>
    /// The eight-bit counterpart of FourColumnsWide. Each load reads exactly eight bytes, including at the
    /// end of the last column, and each column preserves DotWide's accumulation order.
    /// </summary>
    private static void FourColumns8Wide(
        float[] a, int aOffset, OnnxBlockQuantizedWeight weight, float[] c, int target, int column)
    {
        ref float pa = ref MemoryMarshal.GetArrayDataReference(a);
        ref byte pb = ref MemoryMarshal.GetArrayDataReference(weight.Packed);
        int blocks = weight.BlockCount;
        int blockSize = weight.BlockSize;
        int columnBytes = blocks * weight.BlobSize;
        int firstBlob = column * columnBytes;
        int firstScale = column * blocks;
        Vector256<float> sum0 = Vector256<float>.Zero;
        Vector256<float> sum1 = Vector256<float>.Zero;
        Vector256<float> sum2 = Vector256<float>.Zero;
        Vector256<float> sum3 = Vector256<float>.Zero;

        for (int block = 0, k0 = 0; k0 < weight.Reduction; block++, k0 += blockSize)
        {
            int end = Math.Min(blockSize, weight.Reduction - k0);
            int blob = firstBlob + block * weight.BlobSize;
            Vector256<float> scale0 = Vector256.Create(weight.Scales[firstScale + block]);
            Vector256<float> zero0 = Vector256.Create(ZeroPoint(weight, column, block));
            Vector256<float> scale1 = Vector256.Create(weight.Scales[firstScale + blocks + block]);
            Vector256<float> zero1 = Vector256.Create(ZeroPoint(weight, column + 1, block));
            Vector256<float> scale2 = Vector256.Create(weight.Scales[firstScale + 2 * blocks + block]);
            Vector256<float> zero2 = Vector256.Create(ZeroPoint(weight, column + 2, block));
            Vector256<float> scale3 = Vector256.Create(weight.Scales[firstScale + 3 * blocks + block]);
            Vector256<float> zero3 = Vector256.Create(ZeroPoint(weight, column + 3, block));
            for (int i = 0; i < end; i += 8)
            {
                Vector256<float> left = Vector256.LoadUnsafe(ref pa, (nuint)(aOffset + k0 + i));
                int at = blob + i;
                sum0 = Fma.MultiplyAdd(left, (ReadEightBytes(ref pb, at) - zero0) * scale0, sum0);
                sum1 = Fma.MultiplyAdd(left, (ReadEightBytes(ref pb, at + columnBytes) - zero1) * scale1, sum1);
                sum2 = Fma.MultiplyAdd(left, (ReadEightBytes(ref pb, at + 2 * columnBytes) - zero2) * scale2, sum2);
                sum3 = Fma.MultiplyAdd(left, (ReadEightBytes(ref pb, at + 3 * columnBytes) - zero3) * scale3, sum3);
            }
        }

        c[target] = Vector256.Sum(sum0) + (weight.Bias == null ? 0f : weight.Bias[column]);
        c[target + 1] = Vector256.Sum(sum1) + (weight.Bias == null ? 0f : weight.Bias[column + 1]);
        c[target + 2] = Vector256.Sum(sum2) + (weight.Bias == null ? 0f : weight.Bias[column + 2]);
        c[target + 3] = Vector256.Sum(sum3) + (weight.Bias == null ? 0f : weight.Bias[column + 3]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> ReadEightBytes(ref byte packed, int offset) =>
        Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(
            Vector128.CreateScalarUnsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref packed, offset))).AsByte()));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DotWide(float[] a, int aOffset, OnnxBlockQuantizedWeight weight, int column)
    {
        ref float pa = ref MemoryMarshal.GetArrayDataReference(a);
        ref byte pb = ref MemoryMarshal.GetArrayDataReference(weight.Packed);

        int k = weight.Reduction;
        int blockSize = weight.BlockSize;
        int blobSize = weight.BlobSize;
        int blobBase = column * weight.BlockCount * blobSize;
        int scaleBase = column * weight.BlockCount;
        bool eightBit = weight.Bits == 8;
        Vector256<uint> mask = Vector256.Create(0x0000000Fu);
        Vector256<float> accumulated = Vector256<float>.Zero;
        float tail = 0f;

        for (int block = 0, k0 = 0; k0 < k; block++, k0 += blockSize)
        {
            int length = Math.Min(blockSize, k - k0);
            Vector256<float> scale = Vector256.Create(weight.Scales[scaleBase + block]);
            Vector256<float> zeroPoint = Vector256.Create(ZeroPoint(weight, column, block));
            int blob = blobBase + (block * blobSize);

            int i = 0;
            for (; i + 8 <= length; i += 8)
            {
                // EIGHT BYTES ARE READ, NOT SIXTEEN. A 128-bit load would walk up to eight bytes past the end
                // of the weight on its very last block; reading the eight this needs as one 64-bit value and
                // widening them cannot.
                Vector256<float> quantized = eightBit
                    ? Avx.ConvertToVector256Single(
                        Avx2.ConvertToVector256Int32(
                            Vector128.CreateScalarUnsafe(
                                Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref pb, (nuint)(blob + i))))
                                .AsByte()))
                    : Avx.ConvertToVector256Single(
                        Avx2.And(
                            Avx2.ShiftRightLogicalVariable(
                                Vector256.Create(
                                    Unsafe.ReadUnaligned<uint>(
                                        ref Unsafe.Add(ref pb, (nuint)(blob + (i >> 1))))),
                                NibbleShifts),
                            mask).AsInt32());

                accumulated = Fma.MultiplyAdd(
                    Vector256.LoadUnsafe(ref pa, (nuint)(aOffset + k0 + i)),
                    (quantized - zeroPoint) * scale,
                    accumulated);
            }

            // A block is a power of two of at least sixteen, so only the LAST block of a reduction that is
            // not a whole number of blocks can leave anything behind here.
            for (; i < length; i++)
            {
                int value = eightBit
                    ? weight.Packed[blob + i]
                    : ((i & 1) == 0
                        ? weight.Packed[blob + (i >> 1)] & 0x0F
                        : weight.Packed[blob + (i >> 1)] >> 4);
                tail += a[aOffset + k0 + i] * ((value - zeroPoint.GetElement(0)) * scale.GetElement(0));
            }
        }

        return Vector256.Sum(accumulated) + tail;
    }
}

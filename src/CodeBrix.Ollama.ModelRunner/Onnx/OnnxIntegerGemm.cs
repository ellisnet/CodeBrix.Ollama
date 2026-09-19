using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner; //was previously: onnxruntime/core/providers/cpu/quantization/matmul_integer.cc@v1.30.0

/// <summary>
/// The 8-bit integer matrix multiplication <c>MatMulInteger</c> is: each side has a zero point subtracted from
/// it, the products are summed in 32-bit integers, and nothing is rounded because nothing is approximated.
/// </summary>
/// <remarks>
/// <para>
/// THE WEIGHT IS NEVER WIDENED. It is turned round to [N, K] when the model is loaded and stays ONE BYTE PER
/// ELEMENT, which is the whole reason a graph was quantized in the first place; the vector path widens sixteen
/// of those bytes at a time as it reads them. The left-hand side - the activation, a handful of rows - IS
/// widened, once per run, because it is small and because doing so lets the reduction be a single
/// multiply-add-adjacent instruction per sixteen elements.
/// </para>
/// <para>
/// AN UNSIGNED WEIGHT IS STORED SIGNED. Subtracting a zero point makes the two kinds the same arithmetic:
/// <c>b - z</c> with both unsigned equals <c>(b - 128) - (z - 128)</c> with both signed, and <c>b - 128</c> is
/// exactly what the byte's own bits mean read as a signed number. So the loader turns an unsigned weight into
/// a signed one by shifting the zero point instead, and there is one kernel rather than four.
/// </para>
/// <para>
/// THE ARITHMETIC IS EXACT, so the three kernel paths give IDENTICAL answers rather than answers that agree to
/// within a rounding. That is also why there is no separate portable-vector path: .NET's portable
/// <c>Vector</c> offers no 8-bit widening multiply-add to build the wide path out of, so asking for the
/// portable path gets the scalar one and the same numbers. Making that path fast on a processor without AVX2
/// is a performance question and not a correctness one.
/// </para>
/// <para>
/// The products cannot overflow: each side lies in [-255, 255] once its zero point is off, so a product is at
/// most 65,025, and a reduction of 4,096 of them is under 267 million - comfortably inside the 32-bit integer
/// the specification promises to accumulate in.
/// </para>
/// </remarks>
internal static class OnnxIntegerGemm
{
    /// <summary>
    /// C[m, n] = sum over k of (A[m, k] - <paramref name="leftZeroPoint"/>) times
    /// (B[n, k] - the column's zero point), with B already turned round to [n, k].
    /// </summary>
    /// <param name="left">The left-hand elements, widened to 16-bit integers.</param>
    /// <param name="packed">The right-hand elements as signed bytes in [n, k] order.</param>
    /// <param name="result">Where the products go, [m, n] row-major.</param>
    /// <param name="leftZeroPoint">What comes off every left-hand element.</param>
    /// <param name="rightZeroPoints">What comes off each column of the right-hand side; one element or n of them.</param>
    /// <param name="m">The number of left-hand rows.</param>
    /// <param name="k">The length of the reduction.</param>
    /// <param name="n">The number of weight rows, which is the width of the result.</param>
    /// <param name="kind">Which arithmetic path to take.</param>
    /// <param name="threads">How many threads to spread the work over.</param>
    internal static void Multiply(
        short[] left,
        sbyte[] packed,
        int[] result,
        int leftZeroPoint,
        int[] rightZeroPoints,
        int m,
        int k,
        int n,
        OnnxKernelKind kind,
        int threads)
    {
        long total = (long)m * n;
        if (total == 0) return;

        long work = total * Math.Max(k, 1);
        int workers = work >= OnnxGemm.ParallelThreshold
            ? Math.Min(threads, (int)Math.Min(total, int.MaxValue))
            : 1;

        if (workers <= 1)
        {
            Range(left, packed, result, leftZeroPoint, rightZeroPoints, k, n, 0, total, kind);
            return;
        }

        long chunk = (total + workers - 1) / workers;
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
        {
            long start = worker * chunk;
            long end = Math.Min(total, start + chunk);
            if (start < end)
            {
                Range(left, packed, result, leftZeroPoint, rightZeroPoints, k, n, start, end, kind);
            }
        });
    }

    /// <summary>Widens a run of unsigned bytes into 16-bit integers.</summary>
    /// <param name="source">The bytes.</param>
    /// <param name="count">How many of them to widen.</param>
    /// <returns>The widened values.</returns>
    internal static short[] Widen(byte[] source, int count)
    {
        short[] values = new short[count];
        for (int i = 0; i < count; i++) values[i] = source[i];
        return values;
    }

    /// <summary>Widens a run of signed bytes into 16-bit integers.</summary>
    /// <param name="source">The bytes.</param>
    /// <param name="count">How many of them to widen.</param>
    /// <returns>The widened values.</returns>
    internal static short[] Widen(sbyte[] source, int count)
    {
        short[] values = new short[count];
        for (int i = 0; i < count; i++) values[i] = source[i];
        return values;
    }

    /// <summary>
    /// Turns a [k, n] matrix of bytes into an [n, k] one of signed bytes, reading each byte as signed or as
    /// unsigned shifted down by 128.
    /// </summary>
    /// <param name="source">The matrix to read, one byte per element.</param>
    /// <param name="signed">Whether the bytes are already signed.</param>
    /// <param name="k">The number of rows in the source.</param>
    /// <param name="n">The number of columns in the source.</param>
    /// <returns>The turned-round matrix, one byte per element.</returns>
    internal static sbyte[] TransposeToSigned(Array source, bool signed, int k, int n)
    {
        sbyte[] target = new sbyte[(long)k * n];
        byte[] unsignedSource = signed ? null : (byte[])source;
        sbyte[] signedSource = signed ? (sbyte[])source : null;

        const int Block = 32;
        for (int i0 = 0; i0 < k; i0 += Block)
        {
            int iEnd = Math.Min(k, i0 + Block);
            for (int j0 = 0; j0 < n; j0 += Block)
            {
                int jEnd = Math.Min(n, j0 + Block);
                for (int i = i0; i < iEnd; i++)
                {
                    int row = i * n;
                    for (int j = j0; j < jEnd; j++)
                    {
                        target[(j * k) + i] = signed
                            ? signedSource[row + j]
                            : unchecked((sbyte)(unsignedSource[row + j] - 128));
                    }
                }
            }
        }

        return target;
    }

    private static void Range(
        short[] left, sbyte[] packed, int[] result, int leftZeroPoint, int[] rightZeroPoints,
        int k, int n, long start, long end, OnnxKernelKind kind)
    {
        bool wide = kind == OnnxKernelKind.Avx2 && Avx2.IsSupported;
        int row = (int)(start / n);
        int column = (int)(start % n);

        for (long index = start; index < end; index++)
        {
            int rightZeroPoint = rightZeroPoints.Length == 1 ? rightZeroPoints[0] : rightZeroPoints[column];
            result[(int)index] = wide
                ? DotAvx2(left, row * k, packed, column * k, k, leftZeroPoint, rightZeroPoint)
                : DotScalar(left, row * k, packed, column * k, k, leftZeroPoint, rightZeroPoint);

            if (++column == n)
            {
                column = 0;
                row++;
            }
        }
    }

    private static int DotScalar(
        short[] x, int xOffset, sbyte[] y, int yOffset, int k, int xZeroPoint, int yZeroPoint)
    {
        int sum = 0;
        for (int i = 0; i < k; i++)
        {
            sum += (x[xOffset + i] - xZeroPoint) * (y[yOffset + i] - yZeroPoint);
        }

        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DotAvx2(
        short[] x, int xOffset, sbyte[] y, int yOffset, int k, int xZeroPoint, int yZeroPoint)
    {
        ref short px = ref MemoryMarshal.GetArrayDataReference(x);
        ref sbyte py = ref MemoryMarshal.GetArrayDataReference(y);
        Vector256<short> xOffsetVector = Vector256.Create((short)xZeroPoint);
        Vector256<short> yOffsetVector = Vector256.Create((short)yZeroPoint);
        Vector256<int> accumulated = Vector256<int>.Zero;

        int i = 0;
        for (; i + 16 <= k; i += 16)
        {
            Vector256<short> a = Vector256.LoadUnsafe(ref px, (nuint)(xOffset + i)) - xOffsetVector;
            Vector256<short> b =
                Avx2.ConvertToVector256Int16(Vector128.LoadUnsafe(ref py, (nuint)(yOffset + i))) - yOffsetVector;

            // One instruction multiplies sixteen pairs and adds them in pairs into eight 32-bit lanes; the
            // horizontal sum at the end finishes the reduction.
            accumulated += Avx2.MultiplyAddAdjacent(a, b);
        }

        int sum = Vector256.Sum(accumulated);
        for (; i < k; i++)
        {
            sum += (x[xOffset + i] - xZeroPoint) * (y[yOffset + i] - yZeroPoint);
        }

        return sum;
    }
}

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The float matrix multiplications everything else is built on, in three arithmetic paths that compute the
/// same thing: 256-bit AVX2 with fused multiply-add, .NET's portable vectors, and one element at a time.
/// </summary>
/// <remarks>
/// <para>
/// THERE ARE TWO WEIGHT LAYOUTS AND THE CHOICE MATTERS. A graph stores a matrix multiply's right-hand side as
/// [K, N], reduction down its rows. A weight that is a CONSTANT is turned round to [N, K] when the model is
/// loaded, so each output element becomes one contiguous dot product: the same arithmetic, but every thread
/// owns whole output elements and never writes into another thread's cache line, which is worth several times
/// the throughput once more than one thread is at work. A right-hand side that is computed - attention's own
/// scores and values - is used where it lies, in the [K, N] layout, because turning it round every step would
/// cost more than it saves.
/// </para>
/// <para>
/// THE THREE PATHS DO NOT SUM IN THE SAME ORDER, so a long dot product can differ in its last bits between
/// them, exactly as it does between any two matrix libraries. That is floating-point reassociation and not a
/// defect; the suite pins the difference rather than pretending it is not there.
/// </para>
/// <para>
/// A PROMPT IS WORKED OUT COLUMN BY COLUMN, NOT ROW BY ROW. Against a turned-round weight, one column of the
/// result is one row of the weight, and a decoder's weight row is several kilobytes; walking the result by
/// rows reads a different weight row for every output and reads the whole weight again for every row of the
/// prompt, which for a prompt of any length means reading a model's worth of weights out of memory dozens of
/// times. Walking it by columns reads each weight row ONCE and multiplies every row of the prompt by it while
/// it is still in the nearest cache. Each output element is still the same dot product, summed in the same
/// order, so the answer does not change by a single bit - only the order the answers are worked out in does.
/// </para>
/// <para>
/// Nothing here uses the <c>unsafe</c> keyword. The vector loads go through <c>Vector256.LoadUnsafe</c> and
/// <c>MemoryMarshal.GetArrayDataReference</c>, which generate the same code without a pinned pointer.
/// </para>
/// </remarks>
internal static class OnnxGemm
{
    /// <summary>
    /// How much arithmetic a matrix multiply has to be worth before it is spread over threads. Below this,
    /// handing work to the thread pool costs more than doing it.
    /// </summary>
    internal const long ParallelThreshold = 96 * 1024;

    /// <summary>
    /// How many bytes of the left-hand side a prompt keeps hot while it walks the weight: the rows are taken
    /// in blocks of about this size, so that the block stays in the second-level cache for the whole walk.
    /// </summary>
    internal const int RowBlockBytes = 192 * 1024;

    /// <summary>
    /// C[m, n] = A[m, k] times the transpose of the packed weight, which is stored as [n, k].
    /// </summary>
    /// <param name="a">The left-hand elements.</param>
    /// <param name="aOffset">Where the left-hand matrix starts in <paramref name="a"/>.</param>
    /// <param name="packed">The weight, already turned round to [n, k].</param>
    /// <param name="c">The elements to write.</param>
    /// <param name="cOffset">Where the result starts in <paramref name="c"/>.</param>
    /// <param name="m">The number of left-hand rows.</param>
    /// <param name="k">The length of the reduction.</param>
    /// <param name="n">The number of weight rows, which is the width of the result.</param>
    /// <param name="kind">Which arithmetic path to take.</param>
    /// <param name="threads">How many threads to spread the work over.</param>
    internal static void MultiplyPacked(
        float[] a, int aOffset, float[] packed, float[] c, int cOffset,
        int m, int k, int n, OnnxKernelKind kind, int threads)
    {
        if (OnnxMatrixGemm.CanUse(m, k, n, kind))
        {
            OnnxMatrixGemm.MultiplyPacked(a, aOffset, packed, c, cOffset, m, k, n, threads);
            return;
        }
        long total = (long)m * n;
        if (total == 0) return;

        // The work is split by COLUMN, which is by weight row: a worker that owns a column owns every row of
        // the result in it, and reads that weight row once for all of them.
        long work = total * Math.Max(k, 1);
        int workers = work >= ParallelThreshold ? Math.Min(threads, n) : 1;
        if (workers <= 1)
        {
            PackedColumns(a, aOffset, packed, c, cOffset, m, k, n, 0, n, kind);
            return;
        }

        int chunk = (n + workers - 1) / workers;
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
        {
            int start = worker * chunk;
            int end = Math.Min(n, start + chunk);
            if (start < end) PackedColumns(a, aOffset, packed, c, cOffset, m, k, n, start, end, kind);
        });
    }

    /// <summary>
    /// C[m, n] = A[m, k] times B[k, n], with B in the layout the graph stores it in.
    /// </summary>
    /// <param name="a">The left-hand elements.</param>
    /// <param name="aOffset">Where the left-hand matrix starts in <paramref name="a"/>.</param>
    /// <param name="b">The right-hand elements.</param>
    /// <param name="bOffset">Where the right-hand matrix starts in <paramref name="b"/>.</param>
    /// <param name="c">The elements to write.</param>
    /// <param name="cOffset">Where the result starts in <paramref name="c"/>.</param>
    /// <param name="m">The number of left-hand rows.</param>
    /// <param name="k">The length of the reduction.</param>
    /// <param name="n">The width of the result.</param>
    /// <param name="kind">Which arithmetic path to take.</param>
    /// <param name="threads">How many threads to spread the rows over.</param>
    internal static void Multiply(
        float[] a, int aOffset, float[] b, int bOffset, float[] c, int cOffset,
        int m, int k, int n, OnnxKernelKind kind, int threads)
    {
        if ((long)m * n == 0) return;

        long work = (long)m * n * Math.Max(k, 1);
        int workers = work >= ParallelThreshold ? Math.Min(threads, m) : 1;
        if (workers <= 1)
        {
            StandardRows(a, aOffset, b, bOffset, c, cOffset, k, n, 0, m, kind);
            return;
        }

        int chunk = (m + workers - 1) / workers;
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
        {
            int start = worker * chunk;
            int end = Math.Min(m, start + chunk);
            if (start < end) StandardRows(a, aOffset, b, bOffset, c, cOffset, k, n, start, end, kind);
        });
    }

    /// <summary>The dot product of two contiguous runs of floats.</summary>
    /// <param name="x">The first array.</param>
    /// <param name="xOffset">Where the first run starts.</param>
    /// <param name="y">The second array.</param>
    /// <param name="yOffset">Where the second run starts.</param>
    /// <param name="k">How many elements to multiply.</param>
    /// <param name="kind">Which arithmetic path to take.</param>
    /// <returns>The sum of the products.</returns>
    internal static float Dot(float[] x, int xOffset, float[] y, int yOffset, int k, OnnxKernelKind kind)
    {
        ref float px = ref MemoryMarshal.GetArrayDataReference(x);
        ref float py = ref MemoryMarshal.GetArrayDataReference(y);
        return kind switch
        {
            OnnxKernelKind.Avx2 => DotAvx2(ref px, xOffset, ref py, yOffset, k),
            OnnxKernelKind.Vector => DotVector(x, xOffset, y, yOffset, k),
            _ => DotScalar(x, xOffset, y, yOffset, k),
        };
    }

    /// <summary>Turns a [rows, columns] matrix into a [columns, rows] one.</summary>
    /// <param name="source">The matrix to read.</param>
    /// <param name="target">The matrix to write, of the same element count.</param>
    /// <param name="rows">The number of rows in the source.</param>
    /// <param name="columns">The number of columns in the source.</param>
    internal static void Transpose(float[] source, float[] target, int rows, int columns)
    {
        // A blocked walk, so neither side of the copy sweeps a whole cache line away between uses.
        const int Block = 32;
        for (int i0 = 0; i0 < rows; i0 += Block)
        {
            int iEnd = Math.Min(rows, i0 + Block);
            for (int j0 = 0; j0 < columns; j0 += Block)
            {
                int jEnd = Math.Min(columns, j0 + Block);
                for (int i = i0; i < iEnd; i++)
                {
                    int sourceRow = i * columns;
                    for (int j = j0; j < jEnd; j++)
                    {
                        target[(j * rows) + i] = source[sourceRow + j];
                    }
                }
            }
        }
    }

    private static void PackedColumns(
        float[] a, int aOffset, float[] packed, float[] c, int cOffset,
        int m, int k, int n, int firstColumn, int lastColumn, OnnxKernelKind kind)
    {
        ref float pa = ref MemoryMarshal.GetArrayDataReference(a);
        ref float pb = ref MemoryMarshal.GetArrayDataReference(packed);

        if (m > 1)
        {
            // A LONG PROMPT TAKES ITS ROWS IN BLOCKS. Walking the whole weight once for all of a prompt's
            // rows is right until the rows themselves stop fitting in the second-level cache, and then each
            // column re-reads them all. A block of rows that does fit is walked against the whole weight, and
            // the weight is read once per BLOCK rather than once per row - for a prompt of fifty positions
            // that is one block and one walk, and for one of five hundred it is a handful of each.
            int rows = Math.Max(1, RowBlockBytes / Math.Max(k * 4, 1));
            if (rows < m)
            {
                for (int row0 = 0; row0 < m; row0 += rows)
                {
                    int rowEnd = Math.Min(m, row0 + rows);
                    PackedBlock(
                        a, aOffset, packed, c, cOffset, row0, rowEnd, k, n, firstColumn, lastColumn, kind);
                }

                return;
            }

            PackedBlock(a, aOffset, packed, c, cOffset, 0, m, k, n, firstColumn, lastColumn, kind);
            return;
        }

        int first = firstColumn;
        if (kind == OnnxKernelKind.Avx2)
        {
            // GENERATING A TOKEN IS ONE ROW AGAINST THE WHOLE WEIGHT, and then nothing is read twice: every
            // weight row is read once, used once and thrown away, so the rate is set by how fast the memory
            // can be pulled in. Two weight rows are therefore worked on at a time - the left-hand row is
            // loaded once for both, and two independent runs of memory are in flight instead of one, which is
            // what a processor needs to keep its load units busy. Each dot product is the same arithmetic in
            // the same order as one worked out alone.
            for (; first + 2 <= lastColumn; first += 2)
            {
                DotAvx2Pair(
                    ref pa, aOffset, ref pb, first * k, (first + 1) * k, k,
                    out float left, out float right);
                c[cOffset + first] = left;
                c[cOffset + first + 1] = right;
            }
        }

        for (int column = first; column < lastColumn; column++)
        {
            int right = column * k;
            c[cOffset + column] = kind switch
            {
                OnnxKernelKind.Avx2 => DotAvx2(ref pa, aOffset, ref pb, right, k),
                OnnxKernelKind.Vector => DotVector(a, aOffset, packed, right, k),
                _ => DotScalar(a, aOffset, packed, right, k),
            };
        }
    }

    private static void PackedBlock(
        float[] a, int aOffset, float[] packed, float[] c, int cOffset,
        int firstRow, int lastRow, int k, int n, int firstColumn, int lastColumn, OnnxKernelKind kind)
    {
        ref float pa = ref MemoryMarshal.GetArrayDataReference(a);
        ref float pb = ref MemoryMarshal.GetArrayDataReference(packed);

        for (int column = firstColumn; column < lastColumn; column++)
        {
            int right = column * k;
            int target = cOffset + column;
            for (int row = firstRow; row < lastRow; row++)
            {
                int left = aOffset + (row * k);
                c[target + (row * n)] = kind switch
                {
                    OnnxKernelKind.Avx2 => DotAvx2(ref pa, left, ref pb, right, k),
                    OnnxKernelKind.Vector => DotVector(a, left, packed, right, k),
                    _ => DotScalar(a, left, packed, right, k),
                };
            }
        }
    }

    private static void StandardRows(
        float[] a, int aOffset, float[] b, int bOffset, float[] c, int cOffset,
        int k, int n, int firstRow, int lastRow, OnnxKernelKind kind)
    {
        switch (kind)
        {
            case OnnxKernelKind.Avx2:
                StandardRowsAvx2(a, aOffset, b, bOffset, c, cOffset, k, n, firstRow, lastRow);
                return;
            case OnnxKernelKind.Vector:
                StandardRowsVector(a, aOffset, b, bOffset, c, cOffset, k, n, firstRow, lastRow);
                return;
            default:
                StandardRowsScalar(a, aOffset, b, bOffset, c, cOffset, k, n, firstRow, lastRow);
                return;
        }
    }

    private static void StandardRowsScalar(
        float[] a, int aOffset, float[] b, int bOffset, float[] c, int cOffset,
        int k, int n, int firstRow, int lastRow)
    {
        for (int i = firstRow; i < lastRow; i++)
        {
            int target = cOffset + (i * n);
            Array.Clear(c, target, n);
            int left = aOffset + (i * k);
            for (int p = 0; p < k; p++)
            {
                float value = a[left + p];
                int source = bOffset + (p * n);
                for (int j = 0; j < n; j++)
                {
                    c[target + j] += value * b[source + j];
                }
            }
        }
    }

    private static void StandardRowsVector(
        float[] a, int aOffset, float[] b, int bOffset, float[] c, int cOffset,
        int k, int n, int firstRow, int lastRow)
    {
        int width = Vector<float>.Count;
        for (int i = firstRow; i < lastRow; i++)
        {
            int target = cOffset + (i * n);
            Array.Clear(c, target, n);
            int left = aOffset + (i * k);
            for (int p = 0; p < k; p++)
            {
                float value = a[left + p];
                Vector<float> broadcast = new Vector<float>(value);
                int source = bOffset + (p * n);
                int j = 0;
                for (; j + width <= n; j += width)
                {
                    Vector<float> accumulated = new Vector<float>(c, target + j)
                        + (broadcast * new Vector<float>(b, source + j));
                    accumulated.CopyTo(c, target + j);
                }

                for (; j < n; j++)
                {
                    c[target + j] += value * b[source + j];
                }
            }
        }
    }

    private static void StandardRowsAvx2(
        float[] a, int aOffset, float[] b, int bOffset, float[] c, int cOffset,
        int k, int n, int firstRow, int lastRow)
    {
        ref float pb = ref MemoryMarshal.GetArrayDataReference(b);
        ref float pc = ref MemoryMarshal.GetArrayDataReference(c);

        for (int i = firstRow; i < lastRow; i++)
        {
            int target = cOffset + (i * n);
            Array.Clear(c, target, n);
            int left = aOffset + (i * k);
            for (int p = 0; p < k; p++)
            {
                float value = a[left + p];
                Vector256<float> broadcast = Vector256.Create(value);
                int source = bOffset + (p * n);
                int j = 0;
                for (; j + 16 <= n; j += 16)
                {
                    Vector256<float> first = Fma.MultiplyAdd(
                        broadcast,
                        Vector256.LoadUnsafe(ref pb, (nuint)(source + j)),
                        Vector256.LoadUnsafe(ref pc, (nuint)(target + j)));
                    Vector256<float> second = Fma.MultiplyAdd(
                        broadcast,
                        Vector256.LoadUnsafe(ref pb, (nuint)(source + j + 8)),
                        Vector256.LoadUnsafe(ref pc, (nuint)(target + j + 8)));
                    first.StoreUnsafe(ref pc, (nuint)(target + j));
                    second.StoreUnsafe(ref pc, (nuint)(target + j + 8));
                }

                for (; j + 8 <= n; j += 8)
                {
                    Fma.MultiplyAdd(
                        broadcast,
                        Vector256.LoadUnsafe(ref pb, (nuint)(source + j)),
                        Vector256.LoadUnsafe(ref pc, (nuint)(target + j)))
                        .StoreUnsafe(ref pc, (nuint)(target + j));
                }

                for (; j < n; j++)
                {
                    c[target + j] += value * b[source + j];
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DotAvx2(ref float x, int xOffset, ref float y, int yOffset, int k)
    {
        Vector256<float> first = Vector256<float>.Zero;
        Vector256<float> second = Vector256<float>.Zero;
        Vector256<float> third = Vector256<float>.Zero;
        Vector256<float> fourth = Vector256<float>.Zero;
        int i = 0;
        for (; i + 32 <= k; i += 32)
        {
            nuint left = (nuint)(xOffset + i);
            nuint right = (nuint)(yOffset + i);
            first = Fma.MultiplyAdd(
                Vector256.LoadUnsafe(ref x, left), Vector256.LoadUnsafe(ref y, right), first);
            second = Fma.MultiplyAdd(
                Vector256.LoadUnsafe(ref x, left + 8), Vector256.LoadUnsafe(ref y, right + 8), second);
            third = Fma.MultiplyAdd(
                Vector256.LoadUnsafe(ref x, left + 16), Vector256.LoadUnsafe(ref y, right + 16), third);
            fourth = Fma.MultiplyAdd(
                Vector256.LoadUnsafe(ref x, left + 24), Vector256.LoadUnsafe(ref y, right + 24), fourth);
        }

        for (; i + 8 <= k; i += 8)
        {
            first = Fma.MultiplyAdd(
                Vector256.LoadUnsafe(ref x, (nuint)(xOffset + i)),
                Vector256.LoadUnsafe(ref y, (nuint)(yOffset + i)),
                first);
        }

        float sum = Vector256.Sum(first + second + third + fourth);
        for (; i < k; i++)
        {
            sum += Unsafe.Add(ref x, xOffset + i) * Unsafe.Add(ref y, yOffset + i);
        }

        return sum;
    }

    /// <summary>
    /// Two dot products against the same left-hand run, worked out together: the same arithmetic in the same
    /// order as two separate calls, with the left-hand side loaded once instead of twice.
    /// </summary>
    private static void DotAvx2Pair(
        ref float x, int xOffset, ref float y, int firstOffset, int secondOffset, int k,
        out float first, out float second)
    {
        Vector256<float> a0 = Vector256<float>.Zero;
        Vector256<float> a1 = Vector256<float>.Zero;
        Vector256<float> a2 = Vector256<float>.Zero;
        Vector256<float> a3 = Vector256<float>.Zero;
        Vector256<float> b0 = Vector256<float>.Zero;
        Vector256<float> b1 = Vector256<float>.Zero;
        Vector256<float> b2 = Vector256<float>.Zero;
        Vector256<float> b3 = Vector256<float>.Zero;

        int i = 0;
        for (; i + 32 <= k; i += 32)
        {
            nuint left = (nuint)(xOffset + i);
            nuint one = (nuint)(firstOffset + i);
            nuint two = (nuint)(secondOffset + i);
            Vector256<float> x0 = Vector256.LoadUnsafe(ref x, left);
            Vector256<float> x1 = Vector256.LoadUnsafe(ref x, left + 8);
            Vector256<float> x2 = Vector256.LoadUnsafe(ref x, left + 16);
            Vector256<float> x3 = Vector256.LoadUnsafe(ref x, left + 24);

            a0 = Fma.MultiplyAdd(x0, Vector256.LoadUnsafe(ref y, one), a0);
            a1 = Fma.MultiplyAdd(x1, Vector256.LoadUnsafe(ref y, one + 8), a1);
            a2 = Fma.MultiplyAdd(x2, Vector256.LoadUnsafe(ref y, one + 16), a2);
            a3 = Fma.MultiplyAdd(x3, Vector256.LoadUnsafe(ref y, one + 24), a3);
            b0 = Fma.MultiplyAdd(x0, Vector256.LoadUnsafe(ref y, two), b0);
            b1 = Fma.MultiplyAdd(x1, Vector256.LoadUnsafe(ref y, two + 8), b1);
            b2 = Fma.MultiplyAdd(x2, Vector256.LoadUnsafe(ref y, two + 16), b2);
            b3 = Fma.MultiplyAdd(x3, Vector256.LoadUnsafe(ref y, two + 24), b3);
        }

        for (; i + 8 <= k; i += 8)
        {
            Vector256<float> value = Vector256.LoadUnsafe(ref x, (nuint)(xOffset + i));
            a0 = Fma.MultiplyAdd(value, Vector256.LoadUnsafe(ref y, (nuint)(firstOffset + i)), a0);
            b0 = Fma.MultiplyAdd(value, Vector256.LoadUnsafe(ref y, (nuint)(secondOffset + i)), b0);
        }

        float one0 = Vector256.Sum(a0 + a1 + a2 + a3);
        float two0 = Vector256.Sum(b0 + b1 + b2 + b3);
        for (; i < k; i++)
        {
            float value = Unsafe.Add(ref x, xOffset + i);
            one0 += value * Unsafe.Add(ref y, firstOffset + i);
            two0 += value * Unsafe.Add(ref y, secondOffset + i);
        }

        first = one0;
        second = two0;
    }

    private static float DotVector(float[] x, int xOffset, float[] y, int yOffset, int k)
    {
        int width = Vector<float>.Count;
        Vector<float> accumulated = Vector<float>.Zero;
        int i = 0;
        for (; i + width <= k; i += width)
        {
            accumulated += new Vector<float>(x, xOffset + i) * new Vector<float>(y, yOffset + i);
        }

        float sum = Vector.Sum(accumulated);
        for (; i < k; i++)
        {
            sum += x[xOffset + i] * y[yOffset + i];
        }

        return sum;
    }

    private static float DotScalar(float[] x, int xOffset, float[] y, int yOffset, int k)
    {
        float sum = 0f;
        for (int i = 0; i < k; i++)
        {
            sum += x[xOffset + i] * y[yOffset + i];
        }

        return sum;
    }
}

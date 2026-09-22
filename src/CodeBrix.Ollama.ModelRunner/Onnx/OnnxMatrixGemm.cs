using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Matrix-times-matrix path for prompt processing: sixteen output columns in a temporary panel, four
/// activation rows per inner loop. This is the same panel/register reuse strategy used by MLAS SGEMM,
/// implemented here in managed intrinsics. A decoder's single-row path remains in <see cref="OnnxGemm"/>.
/// </summary>
internal static class OnnxMatrixGemm
{
    internal const int Columns = 16;

    internal static bool CanUse(int rows, int reduction, int width, OnnxKernelKind kind) =>
        rows >= 8 && reduction >= 64 && width >= Columns && kind == OnnxKernelKind.Avx2
        && Avx2.IsSupported && Fma.IsSupported;

    internal static void MultiplyPacked(float[] a, int aOffset, float[] weight, float[] c, int cOffset,
        int m, int k, int n, int threads)
    {
        int tiles = (n + Columns - 1) / Columns;
        int workers = Math.Min(threads, tiles);
        void Work(int worker)
        {
            float[] panel = ArrayPool<float>.Shared.Rent(checked(k * Columns));
            try
            {
                int firstTile = tiles * worker / workers;
                int end = tiles * (worker + 1) / workers;
                for (int tile = firstTile; tile < end; tile++)
                {
                    int column = tile * Columns;
                    int width = Math.Min(Columns, n - column);
                    // Read short contiguous pieces of each column. Reading sixteen columns at the
                    // same reduction index conflicts in L1 when K is a power of two.
                    for (int first = 0; first < k; first += 32)
                    {
                        int last = Math.Min(k, first + 32);
                        for (int j = 0; j < width; j++)
                            for (int p = first; p < last; p++) panel[p * Columns + j] = weight[(column + j) * k + p];
                        for (int j = width; j < Columns; j++)
                            for (int p = first; p < last; p++) panel[p * Columns + j] = 0;
                    }
                    MultiplyPanel(a, aOffset, panel, c, cOffset, m, k, n, column, width);
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(panel);
            }
        }
        if (workers == 1) Work(0);
        else Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, Work);
    }

    // Call only after CanUse has checked AVX2 and FMA. All scratch buffers are bounded by K * 16;
    // no second model-sized copy is retained, and each worker owns distinct result columns.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static void MultiplyPanel(float[] a, int aOffset, float[] panel, float[] c, int cOffset,
        int m, int k, int n, int column, int width)
    {
        // An 8 KiB reduction slice stays hot while every activation row consumes it. Full tiles
        // may reload their partial result safely; a tail tile uses the original single pass.
        if (width != Columns)
        {
            MultiplyPanelBlock(a, aOffset, panel, c, cOffset, m, k, n, column, width, 0, k);
            return;
        }
        for (int first = 0; first < k; first += 128)
            MultiplyPanelBlock(a, aOffset, panel, c, cOffset, m, k, n, column, width, first, Math.Min(k, first + 128));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void MultiplyPanelBlock(float[] a, int aOffset, float[] panel, float[] c, int cOffset,
        int m, int k, int n, int column, int width, int first, int last)
    {
        ref float pa = ref MemoryMarshal.GetArrayDataReference(a);
        ref float pb = ref MemoryMarshal.GetArrayDataReference(panel);
        ref float pc = ref MemoryMarshal.GetArrayDataReference(c);
        int row = 0;
        for (; row + 4 <= m; row += 4)
        {
            Vector256<float> c00 = Vector256<float>.Zero, c01 = Vector256<float>.Zero;
            Vector256<float> c10 = Vector256<float>.Zero, c11 = Vector256<float>.Zero;
            Vector256<float> c20 = Vector256<float>.Zero, c21 = Vector256<float>.Zero;
            Vector256<float> c30 = Vector256<float>.Zero, c31 = Vector256<float>.Zero;
            if (first != 0)
            {
                int offset = cOffset + row * n + column;
                c00 = Vector256.LoadUnsafe(ref pc, (nuint)offset);
                c01 = Vector256.LoadUnsafe(ref pc, (nuint)(offset + 8));
                c10 = Vector256.LoadUnsafe(ref pc, (nuint)(offset + n));
                c11 = Vector256.LoadUnsafe(ref pc, (nuint)(offset + n + 8));
                c20 = Vector256.LoadUnsafe(ref pc, (nuint)(offset + 2 * n));
                c21 = Vector256.LoadUnsafe(ref pc, (nuint)(offset + 2 * n + 8));
                c30 = Vector256.LoadUnsafe(ref pc, (nuint)(offset + 3 * n));
                c31 = Vector256.LoadUnsafe(ref pc, (nuint)(offset + 3 * n + 8));
            }
            int left = aOffset + row * k;
            ref float a0 = ref Unsafe.Add(ref pa, left);
            ref float a1 = ref Unsafe.Add(ref a0, k);
            ref float a2 = ref Unsafe.Add(ref a1, k);
            ref float a3 = ref Unsafe.Add(ref a2, k);
            for (int p = first; p < last; p++)
            {
                Vector256<float> b0 = Vector256.LoadUnsafe(ref pb, (nuint)(p * Columns));
                Vector256<float> b1 = Vector256.LoadUnsafe(ref pb, (nuint)(p * Columns + 8));
                Vector256<float> av = Vector256.Create(Unsafe.Add(ref a0, p));
                c00 = Fma.MultiplyAdd(av, b0, c00);
                c01 = Fma.MultiplyAdd(av, b1, c01);
                av = Vector256.Create(Unsafe.Add(ref a1, p));
                c10 = Fma.MultiplyAdd(av, b0, c10);
                c11 = Fma.MultiplyAdd(av, b1, c11);
                av = Vector256.Create(Unsafe.Add(ref a2, p));
                c20 = Fma.MultiplyAdd(av, b0, c20);
                c21 = Fma.MultiplyAdd(av, b1, c21);
                av = Vector256.Create(Unsafe.Add(ref a3, p));
                c30 = Fma.MultiplyAdd(av, b0, c30);
                c31 = Fma.MultiplyAdd(av, b1, c31);
            }
            int target = cOffset + row * n + column;
            Store(ref pc, target, width, c00, c01);
            Store(ref pc, target + n, width, c10, c11);
            Store(ref pc, target + 2 * n, width, c20, c21);
            Store(ref pc, target + 3 * n, width, c30, c31);
        }
        for (; row < m; row++)
        {
            Vector256<float> c0 = Vector256<float>.Zero, c1 = Vector256<float>.Zero;
            if (first != 0)
            {
                int offset = cOffset + row * n + column;
                c0 = Vector256.LoadUnsafe(ref pc, (nuint)offset);
                c1 = Vector256.LoadUnsafe(ref pc, (nuint)(offset + 8));
            }
            int left = aOffset + row * k;
            for (int p = first; p < last; p++)
            {
                Vector256<float> av = Vector256.Create(Unsafe.Add(ref pa, left + p));
                c0 = Fma.MultiplyAdd(av, Vector256.LoadUnsafe(ref pb, (nuint)(p * Columns)), c0);
                c1 = Fma.MultiplyAdd(av, Vector256.LoadUnsafe(ref pb, (nuint)(p * Columns + 8)), c1);
            }
            Store(ref pc, cOffset + row * n + column, width, c0, c1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(ref float c, int offset, int width, Vector256<float> first, Vector256<float> second)
    {
        if (width == Columns)
        {
            first.StoreUnsafe(ref c, (nuint)offset);
            second.StoreUnsafe(ref c, (nuint)(offset + 8));
        }
        else
        {
            for (int i = 0; i < Math.Min(width, 8); i++) Unsafe.Add(ref c, offset + i) = first.GetElement(i);
            for (int i = 8; i < width; i++) Unsafe.Add(ref c, offset + i) = second.GetElement(i - 8);
        }
    }
}

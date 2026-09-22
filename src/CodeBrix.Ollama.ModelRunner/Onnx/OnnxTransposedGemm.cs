using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A matrix product whose right operand is stored as [N,K], optionally scaled as it is read.</summary>
internal static class OnnxTransposedGemm
{
    internal static void Multiply(
        float[] a, int aOffset, float[] b, int bOffset, float[] c, int cOffset,
        int m, int k, int n, OnnxKernelKind kind, int threads, float? scale)
    {
        if (kind == OnnxKernelKind.Avx2 && (!Avx2.IsSupported || !Fma.IsSupported))
            kind = OnnxKernelKind.Vector;
        int total = checked(m * n);
        if (total == 0) return;
        int workers = (long)total * Math.Max(k, 1) >= OnnxGemm.ParallelThreshold
            ? Math.Min(threads, total) : 1;
        if (workers <= 1)
        {
            Range(a, aOffset, b, bOffset, c, cOffset, k, n, 0, total, kind, scale);
            return;
        }

        int chunk = 1 + ((total - 1) / workers);
        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
        {
            int start = worker * chunk;
            Range(a, aOffset, b, bOffset, c, cOffset, k, n, start, Math.Min(start + chunk, total), kind, scale);
        });
    }

    private static void Range(
        float[] a, int aOffset, float[] b, int bOffset, float[] c, int cOffset,
        int k, int n, int start, int end, OnnxKernelKind kind, float? scale)
    {
        for (int i = start; i < end; i++)
        {
            int left = aOffset + (i / n * k);
            int right = bOffset + (i % n * k);
            c[cOffset + i] = scale.HasValue
                ? ScaledDot(a, left, b, right, k, kind, scale.Value)
                : OnnxGemm.Dot(a, left, b, right, k, kind);
        }
    }

    private static float ScaledDot(
        float[] a, int aOffset, float[] b, int bOffset, int k, OnnxKernelKind kind, float scale)
    {
        // Scale each right-hand value before its product, as the original Mul did. Moving the scale to
        // the final sum can change overflow/underflow and is not equivalent IEEE floating-point arithmetic.
        int i = 0;
        float sum = 0f;
        if (kind == OnnxKernelKind.Avx2)
        {
            ref float pa = ref MemoryMarshal.GetArrayDataReference(a);
            ref float pb = ref MemoryMarshal.GetArrayDataReference(b);
            Vector256<float> multiplier = Vector256.Create(scale);
            Vector256<float> first = Vector256<float>.Zero;
            Vector256<float> second = Vector256<float>.Zero;
            for (; i + 16 <= k; i += 16)
            {
                first = Fma.MultiplyAdd(
                    Vector256.LoadUnsafe(ref pa, (nuint)(aOffset + i)),
                    Vector256.LoadUnsafe(ref pb, (nuint)(bOffset + i)) * multiplier, first);
                second = Fma.MultiplyAdd(
                    Vector256.LoadUnsafe(ref pa, (nuint)(aOffset + i + 8)),
                    Vector256.LoadUnsafe(ref pb, (nuint)(bOffset + i + 8)) * multiplier, second);
            }

            for (; i + 8 <= k; i += 8)
            {
                first = Fma.MultiplyAdd(
                    Vector256.LoadUnsafe(ref pa, (nuint)(aOffset + i)),
                    Vector256.LoadUnsafe(ref pb, (nuint)(bOffset + i)) * multiplier, first);
            }

            sum = Vector256.Sum(first + second);
        }
        else if (kind == OnnxKernelKind.Vector)
        {
            int width = Vector<float>.Count;
            Vector<float> multiplier = new Vector<float>(scale);
            Vector<float> accumulated = Vector<float>.Zero;
            for (; i + width <= k; i += width)
                accumulated += new Vector<float>(a, aOffset + i) * (new Vector<float>(b, bOffset + i) * multiplier);
            sum = Vector.Sum(accumulated);
        }

        for (; i < k; i++) sum += a[aOffset + i] * (b[bOffset + i] * scale);
        return sum;
    }
}

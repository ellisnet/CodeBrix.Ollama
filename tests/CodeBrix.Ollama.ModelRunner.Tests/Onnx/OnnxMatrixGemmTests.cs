using System;
using System.Runtime.Intrinsics.X86;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

public sealed class OnnxMatrixGemmTests
{
    [Theory]
    [InlineData(8, 64, 16)]
    [InlineData(11, 101, 23)]
    [InlineData(71, 1024, 32)]
    [InlineData(9, 4096, 17)]
    public void Matrix_panels_match_double_precision_reference_and_keep_offsets(int m, int k, int n)
    {
        if (!Avx2.IsSupported || !Fma.IsSupported) return;
        const int offset = 7;
        var random = new Random(20260922);
        var a = new float[offset + m * k];
        var b = new float[n * k];
        for (int i = 0; i < a.Length; i++) a[i] = (float)(random.NextDouble() - .5);
        for (int i = 0; i < b.Length; i++) b[i] = (float)(random.NextDouble() - .5);
        var actual = new float[offset + m * n + 5];
        Array.Fill(actual, -99);
        OnnxGemm.MultiplyPacked(a, offset, b, actual, offset, m, k, n, OnnxKernelKind.Avx2, 4);
        for (int row = 0; row < m; row++)
        {
            for (int column = 0; column < n; column++)
            {
                double expected = 0;
                for (int p = 0; p < k; p++) expected += (double)a[offset + row * k + p] * b[column * k + p];
                Assert.True(Math.Abs(actual[offset + row * n + column] - expected) < 0.0002,
                    $"[{row},{column}] expected {expected}, got {actual[offset + row * n + column]}");
            }
        }
        for (int i = 0; i < offset; i++) Assert.Equal(-99, actual[i]);
        for (int i = offset + m * n; i < actual.Length; i++) Assert.Equal(-99, actual[i]);
        var single = new float[actual.Length];
        Array.Fill(single, -99);
        OnnxGemm.MultiplyPacked(a, offset, b, single, offset, m, k, n, OnnxKernelKind.Avx2, 1);
        Assert.Equal(single, actual);
    }

    [Fact]
    public void Matrix_path_never_selects_Intel_intrinsics_for_portable_or_scalar_requests()
    {
        Assert.False(OnnxMatrixGemm.CanUse(72, 4096, 1024, OnnxKernelKind.Scalar));
        Assert.False(OnnxMatrixGemm.CanUse(72, 4096, 1024, OnnxKernelKind.Vector));
        Assert.False(OnnxMatrixGemm.CanUse(1, 4096, 1024, OnnxKernelKind.Avx2));
    }
}

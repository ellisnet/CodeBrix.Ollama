using System;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>Large cache concatenations copy disjoint heads correctly when spread across threads.</summary>
public sealed class OnnxConcatKernelTests
{
    /// <summary>Every output head holds its own past followed by its new positions, including an empty past.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(997)]
    public void Run_copies_large_unequal_pieces(int past)
    {
        //Arrange
        const int Heads = 7;
        const int New = 1025;
        const int Width = 64;
        var a = new float[Heads * past * Width];
        var b = new float[Heads * New * Width];
        for (int i = 0; i < a.Length; i++) a[i] = i + 1;
        for (int i = 0; i < b.Length; i++) b[i] = -i - 1;
        var values = new[]
        {
            OnnxValue.Wrap(OnnxElementType.Float, a, new long[] { 1, Heads, past, Width }),
            OnnxValue.Wrap(OnnxElementType.Float, b, new long[] { 1, Heads, New, Width }),
            null,
        };
        var kernel = new OnnxConcatKernel();
        var node = new OnnxPlanNode(0, "Concat", "cache", kernel, new[] { 0, 1 }, new[] { 2 }) { State = -2L };
        var context = new OnnxOperatorContext(values, new OnnxArena(true),
            OnnxExecutionSettings.Resolve(new OnnxRunnerOptions { Threads = 4 }));
        context.Bind(node);

        //Act
        kernel.Run(context);

        //Assert
        values[2].Shape.Should().Equal(1L, Heads, past + New, Width);
        for (int head = 0; head < Heads; head++)
        {
            int offset = head * (past + New) * Width;
            values[2].Floats.AsSpan(offset, past * Width).SequenceEqual(a.AsSpan(head * past * Width, past * Width))
                .Should().BeTrue();
            values[2].Floats.AsSpan(offset + past * Width, New * Width).SequenceEqual(b.AsSpan(head * New * Width, New * Width))
                .Should().BeTrue();
        }
    }
}

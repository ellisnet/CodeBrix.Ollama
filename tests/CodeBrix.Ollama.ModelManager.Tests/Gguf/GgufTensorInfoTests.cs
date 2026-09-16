// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: fs/gguf/tensor.go at commit a43fad18.
using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>Covers the element and byte counting rules of a tensor descriptor.</summary>
public sealed class GgufTensorInfoTests
{
    [Fact]
    public void ElementCount_MultipliesEveryDimension()
    {
        //Arrange
        var tensor = new GgufTensorInfo("token_embd.weight", 0, new ulong[] { 2, 3 }, GgufTensorType.F32);

        //Act
        long elementCount = tensor.ElementCount;

        //Assert
        elementCount.Should().Be(6L);
        tensor.Shape.Should().Equal(new ulong[] { 2, 3 });
    }

    [Fact]
    public void ElementCount_IsOne_ForAScalarTensor()
    {
        //Arrange
        var tensor = new GgufTensorInfo("scalar", 0, Array.Empty<ulong>(), GgufTensorType.F32);

        //Act
        //Assert
        tensor.ElementCount.Should().Be(1L);
        tensor.ByteCount.Should().Be(4L);
    }

    [Theory]
    [InlineData(GgufTensorType.F32, 8UL, 32L)]
    [InlineData(GgufTensorType.F16, 4UL, 8L)]
    [InlineData(GgufTensorType.BF16, 4UL, 8L)]
    [InlineData(GgufTensorType.I8, 16UL, 16L)]
    [InlineData(GgufTensorType.I64, 3UL, 24L)]
    [InlineData(GgufTensorType.Q4_0, 64UL, 36L)]
    [InlineData(GgufTensorType.Q8_0, 32UL, 34L)]
    [InlineData(GgufTensorType.Q4_K, 256UL, 144L)]
    [InlineData(GgufTensorType.Q6_K, 512UL, 420L)]
    [InlineData(GgufTensorType.MXFP4, 32UL, 17L)]
    public void ByteCount_UsesBlockSizeAndTypeSize(GgufTensorType type, ulong length, long expected)
    {
        //Arrange
        var tensor = new GgufTensorInfo("weight", 0, new[] { length }, type);

        //Act
        //Assert
        tensor.ByteCount.Should().Be(expected);
        tensor.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ByteCount_IsMinusOne_WhenTheRowIsNotAWholeNumberOfBlocks()
    {
        //Arrange
        var tensor = new GgufTensorInfo("bad.weight", 0, new ulong[] { 31 }, GgufTensorType.Q4_0);

        //Act
        //Assert
        tensor.ElementCount.Should().Be(31L);
        tensor.ByteCount.Should().Be(-1L);
        tensor.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ElementCount_IsMinusOne_WhenTheShapeOverflows()
    {
        //Arrange
        var tensor = new GgufTensorInfo("bad.weight", 0, new ulong[] { long.MaxValue, 2 }, GgufTensorType.F32);

        //Act
        //Assert
        tensor.ElementCount.Should().Be(-1L);
        tensor.ByteCount.Should().Be(-1L);
        tensor.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ElementCount_IsMinusOne_WhenADimensionExceedsASignedCount()
    {
        //Arrange
        var tensor = new GgufTensorInfo("bad.weight", 0, new[] { ulong.MaxValue }, GgufTensorType.F32);

        //Act
        //Assert
        tensor.ElementCount.Should().Be(-1L);
        tensor.ByteCount.Should().Be(-1L);
    }

    [Fact]
    public void ByteCount_IsMinusOne_WhenTheTypeHasNoDefinedSize()
    {
        //Arrange
        var tensor = new GgufTensorInfo("weight", 0, new ulong[] { 32 }, GgufTensorType.Q4_2);

        //Act
        //Assert
        tensor.ByteCount.Should().Be(-1L);
        tensor.IsValid.Should().BeFalse();
        tensor.TypeName.Should().Be("q4_2");
    }

    [Fact]
    public void IsValid_IsFalse_ForAnUnnamedTensor()
    {
        //Arrange
        var tensor = new GgufTensorInfo(string.Empty, 0, new ulong[] { 4 }, GgufTensorType.F32);

        //Act
        //Assert
        tensor.IsValid.Should().BeFalse();
        tensor.ToString().Should().BeEmpty();
    }

    [Fact]
    public void TypeNameAndToString_DescribeTheTensor()
    {
        //Arrange
        var tensor = new GgufTensorInfo("blk.0.attn_q.weight", 128, new ulong[] { 3, 3 }, GgufTensorType.F16);

        //Act
        //Assert
        tensor.Name.Should().Be("blk.0.attn_q.weight");
        tensor.Offset.Should().Be(128UL);
        tensor.Type.Should().Be(GgufTensorType.F16);
        tensor.TypeName.Should().Be("f16");
        tensor.ToString().Should().Be("blk.0.attn_q.weight");
    }
}

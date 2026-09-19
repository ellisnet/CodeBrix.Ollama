using System;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The public tensor type: it wraps a caller's array without copying it, and it refuses a shape the array
/// cannot hold.
/// </summary>
public sealed class OnnxTensorTests
{
    /// <summary>The array a tensor is built from is the array it carries, not a copy of it.</summary>
    [Fact]
    public void FromFloats_does_not_copy_the_array()
    {
        //Arrange
        var values = new[] { 1f, 2f, 3f, 4f };

        //Act
        var tensor = OnnxTensor.FromFloats(values, 2, 2);

        //Assert
        ReferenceEquals(tensor.Floats, values).Should().BeTrue();
    }

    /// <summary>The element count is the product of the shape.</summary>
    [Fact]
    public void Count_is_the_product_of_the_shape() =>
        OnnxTensor.FromFloats(new float[24], 2, 3, 4).Count.Should().Be(24L);

    /// <summary>A shape with no dimensions is a scalar of one element.</summary>
    [Fact]
    public void Count_of_a_scalar_is_one() =>
        OnnxTensor.FromFloats(new[] { 2.5f }).Count.Should().Be(1L);

    /// <summary>A dimension of nought makes an empty tensor, which is ordinary and not an error.</summary>
    [Fact]
    public void Count_of_an_empty_tensor_is_nought() =>
        OnnxTensor.FromFloats(Array.Empty<float>(), 1, 0, 8).Count.Should().Be(0L);

    /// <summary>The element type says which array is filled in.</summary>
    [Fact]
    public void FromInt64_carries_the_int64_array()
    {
        //Act
        var tensor = OnnxTensor.FromInt64(new long[] { 1, 2 }, 2);

        //Assert
        tensor.ElementType.Should().Be(OnnxElementType.Int64);
        tensor.Int64s.Should().NotBeNull();
        tensor.Floats.Should().BeNull();
    }

    /// <summary>A boolean tensor carries the boolean array and nothing else.</summary>
    [Fact]
    public void FromBooleans_carries_the_bool_array()
    {
        //Act
        var tensor = OnnxTensor.FromBooleans(new[] { true, false }, 2);

        //Assert
        tensor.ElementType.Should().Be(OnnxElementType.Bool);
        tensor.Booleans.Should().HaveCount(2);
    }

    /// <summary>A 32-bit integer tensor carries the int32 array.</summary>
    [Fact]
    public void FromInt32_carries_the_int32_array()
    {
        //Act
        var tensor = OnnxTensor.FromInt32(new[] { 7, 8, 9 }, 3);

        //Assert
        tensor.ElementType.Should().Be(OnnxElementType.Int32);
        tensor.Int32s.Should().HaveCount(3);
    }

    /// <summary>An array too short for the shape is refused.</summary>
    [Fact]
    public void FromFloats_with_too_few_elements_throws()
    {
        //Arrange
        Action act = () => OnnxTensor.FromFloats(new float[3], 2, 2);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A longer array than the shape asks for is allowed: the first elements are the tensor.</summary>
    [Fact]
    public void FromFloats_with_a_longer_array_is_allowed() =>
        OnnxTensor.FromFloats(new float[16], 2, 2).Count.Should().Be(4L);

    /// <summary>A negative dimension is refused.</summary>
    [Fact]
    public void FromFloats_with_a_negative_dimension_throws()
    {
        //Arrange
        Action act = () => OnnxTensor.FromFloats(new float[4], 2, -2);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A null array is refused.</summary>
    [Fact]
    public void FromFloats_with_null_throws()
    {
        //Arrange
        Action act = () => OnnxTensor.FromFloats(null, 1);

        //Act and assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>The shape a tensor reports is its own copy, so a caller's later edit cannot change it.</summary>
    [Fact]
    public void Shape_is_copied_from_the_caller()
    {
        //Arrange
        var shape = new long[] { 2, 2 };
        var tensor = OnnxTensor.FromFloats(new float[4], shape);

        //Act
        shape[0] = 99;

        //Assert
        tensor.Shape[0].Should().Be(2L);
    }

    /// <summary>The description names the element type and the shape.</summary>
    [Fact]
    public void ToString_names_the_type_and_the_shape() =>
        OnnxTensor.FromFloats(new float[6], 1, 2, 3).ToString().Should().Be("float[1,2,3]");
}

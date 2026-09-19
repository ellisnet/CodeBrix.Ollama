using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The shape arithmetic every kernel shares: element counts, strides, negative axes and numpy's bidirectional
/// broadcasting, including the cases an empty tensor produces.
/// </summary>
public sealed class OnnxShapeTests
{
    /// <summary>The element count is the product of the dimensions.</summary>
    [Fact]
    public void ElementCount_multiplies_the_dimensions() =>
        OnnxShape.ElementCount(new long[] { 2, 3, 4 }).Should().Be(24);

    /// <summary>A scalar holds one element.</summary>
    [Fact]
    public void ElementCount_of_a_scalar_is_one() =>
        OnnxShape.ElementCount(System.Array.Empty<long>()).Should().Be(1);

    /// <summary>A dimension of nought empties the tensor.</summary>
    [Fact]
    public void ElementCount_with_an_empty_dimension_is_nought() =>
        OnnxShape.ElementCount(new long[] { 4, 0, 7 }).Should().Be(0);

    /// <summary>Row-major strides run from the innermost dimension outwards.</summary>
    [Fact]
    public void Strides_are_row_major() =>
        OnnxShape.Strides(new long[] { 2, 3, 4 }).Should().Equal(new[] { 12, 4, 1 });

    /// <summary>Two shapes of the same rank broadcast dimension by dimension.</summary>
    [Fact]
    public void Broadcast_stretches_a_dimension_of_one() =>
        OnnxShape.Broadcast(new long[] { 2, 1, 4 }, new long[] { 1, 3, 1 }, "test")
            .Should().Equal(new long[] { 2, 3, 4 });

    /// <summary>A shorter shape is lined up against the end of the longer one.</summary>
    [Fact]
    public void Broadcast_lines_up_from_the_right() =>
        OnnxShape.Broadcast(new long[] { 2, 3, 4 }, new long[] { 4 }, "test")
            .Should().Equal(new long[] { 2, 3, 4 });

    /// <summary>A scalar broadcasts against anything.</summary>
    [Fact]
    public void Broadcast_against_a_scalar_keeps_the_shape() =>
        OnnxShape.Broadcast(new long[] { 5, 6 }, System.Array.Empty<long>(), "test")
            .Should().Equal(new long[] { 5, 6 });

    /// <summary>Dimensions that are neither equal nor one cannot be broadcast.</summary>
    [Fact]
    public void Broadcast_refuses_dimensions_that_do_not_meet()
    {
        //Arrange
        System.Action act = () => OnnxShape.Broadcast(new long[] { 3 }, new long[] { 4 }, "test");

        //Act and assert
        act.Should().Throw<InferenceException>();
    }

    /// <summary>A stretched dimension is walked with a stride of nought.</summary>
    [Fact]
    public void BroadcastStrides_are_nought_where_a_tensor_is_stretched() =>
        OnnxShape.BroadcastStrides(new long[] { 1, 4 }, new long[] { 3, 4 })
            .Should().Equal(new[] { 0, 1 });

    /// <summary>A dimension the shorter tensor does not have is walked with a stride of nought too.</summary>
    [Fact]
    public void BroadcastStrides_are_nought_for_a_dimension_that_is_not_there() =>
        OnnxShape.BroadcastStrides(new long[] { 4 }, new long[] { 2, 3, 4 })
            .Should().Equal(new[] { 0, 0, 1 });

    /// <summary>A negative axis counts from the end.</summary>
    [Fact]
    public void NormalizeAxis_counts_a_negative_axis_from_the_end() =>
        OnnxShape.NormalizeAxis(-1, 4, "test").Should().Be(3);

    /// <summary>An axis outside the rank is refused.</summary>
    [Fact]
    public void NormalizeAxis_refuses_an_axis_outside_the_rank()
    {
        //Arrange
        System.Action act = () => OnnxShape.NormalizeAxis(4, 4, "test");

        //Act and assert
        act.Should().Throw<InferenceException>();
    }

    /// <summary>Two shapes are the same only when both their rank and every dimension agree.</summary>
    [Fact]
    public void SameShape_compares_rank_and_dimensions()
    {
        //Assert
        OnnxShape.SameShape(new long[] { 2, 3 }, new long[] { 2, 3 }).Should().BeTrue();
        OnnxShape.SameShape(new long[] { 2, 3 }, new long[] { 3, 2 }).Should().BeFalse();
        OnnxShape.SameShape(new long[] { 2, 3 }, new long[] { 1, 2, 3 }).Should().BeFalse();
    }

    /// <summary>A shape is spelled out in brackets for a message.</summary>
    [Fact]
    public void Describe_spells_the_shape_out() =>
        OnnxShape.Describe(new long[] { 1, 4, 0, 256 }).Should().Be("[1,4,0,256]");
}

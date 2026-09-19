using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The load's memory accounting: what it counts as abandoned, and when that is worth a collection.
/// </summary>
/// <remarks>
/// The threshold is the whole point of the type. A model that abandons a gigabyte while it is read should be
/// collected several times over; the hundreds of tiny graphs an offline suite loads should never be collected
/// at all, because a forced collection each would cost far more than the few kilobytes it reclaimed.
/// </remarks>
public sealed class OnnxLoadReclaimTests
{
    /// <summary>A float element is four bytes.</summary>
    [Fact]
    public void Bytes_of_a_float_tensor_counts_four_to_the_element() =>
        OnnxLoadReclaim.Bytes(OnnxElementType.Float, 1000).Should().Be(4000L);

    /// <summary>A 64-bit integer element is eight bytes.</summary>
    [Fact]
    public void Bytes_of_an_int64_tensor_counts_eight_to_the_element() =>
        OnnxLoadReclaim.Bytes(OnnxElementType.Int64, 1000).Should().Be(8000L);

    /// <summary>A 32-bit integer element is four bytes.</summary>
    [Fact]
    public void Bytes_of_an_int32_tensor_counts_four_to_the_element() =>
        OnnxLoadReclaim.Bytes(OnnxElementType.Int32, 1000).Should().Be(4000L);

    /// <summary>A quantized or boolean element is one byte.</summary>
    /// <param name="elementType">The element type, as its number: the type itself is internal to the library.</param>
    [Theory]
    [InlineData((int)OnnxElementType.UInt8)]
    [InlineData((int)OnnxElementType.Int8)]
    [InlineData((int)OnnxElementType.Bool)]
    public void Bytes_of_a_single_byte_tensor_counts_one_to_the_element(int elementType) =>
        OnnxLoadReclaim.Bytes((OnnxElementType)elementType, 1000).Should().Be(1000L);

    /// <summary>A small graph is never collected, however many weights it abandons.</summary>
    [Fact]
    public void Abandoned_below_the_threshold_asks_for_no_collection()
    {
        //Arrange
        var reclaim = new OnnxLoadReclaim();

        //Act
        for (int i = 0; i < 64; i++) reclaim.Abandoned(OnnxLoadReclaim.ThresholdBytes / 128);

        //Assert
        reclaim.Collections.Should().Be(0);
    }

    /// <summary>Passing the threshold asks for one collection, not one for every weight after it.</summary>
    [Fact]
    public void Abandoned_past_the_threshold_asks_for_one_collection()
    {
        //Arrange
        var reclaim = new OnnxLoadReclaim();

        //Act
        reclaim.Abandoned(OnnxLoadReclaim.ThresholdBytes);
        reclaim.Abandoned(1024);

        //Assert
        reclaim.Collections.Should().Be(1);
    }

    /// <summary>A model that abandons its own weight several times over is collected several times over.</summary>
    [Fact]
    public void Abandoned_past_the_threshold_three_times_asks_for_three_collections()
    {
        //Arrange
        var reclaim = new OnnxLoadReclaim();

        //Act
        for (int i = 0; i < 3; i++) reclaim.Abandoned(OnnxLoadReclaim.ThresholdBytes);

        //Assert
        reclaim.Collections.Should().Be(3);
    }

    /// <summary>Nothing abandoned is nothing to collect, even when asked directly.</summary>
    [Fact]
    public void Now_with_nothing_abandoned_asks_for_no_collection()
    {
        //Arrange
        var reclaim = new OnnxLoadReclaim();

        //Act
        reclaim.Now();

        //Assert
        reclaim.Collections.Should().Be(0);
    }

    /// <summary>A negative or empty count is not counted.</summary>
    [Fact]
    public void Abandoned_with_nothing_counts_nothing()
    {
        //Arrange
        var reclaim = new OnnxLoadReclaim();

        //Act
        reclaim.Abandoned(0);
        reclaim.Abandoned(-1);
        reclaim.Abandoned(OnnxLoadReclaim.ThresholdBytes - 1);

        //Assert
        reclaim.Collections.Should().Be(0);
    }
}

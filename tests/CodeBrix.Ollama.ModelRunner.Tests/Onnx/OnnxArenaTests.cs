using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The buffer pool a loaded model runs out of: what it hands out again, what it never hands out again, and
/// that a tensor several others are reading is not given away under them.
/// </summary>
public sealed class OnnxArenaTests
{
    /// <summary>A buffer that has been let go is handed out again.</summary>
    [Fact]
    public void Rent_hands_a_returned_buffer_out_again()
    {
        //Arrange
        var arena = new OnnxArena(true);
        var first = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 16 }, true);
        var data = first.Buffer.Data;

        //Act
        first.Release(arena);
        var second = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 16 }, true);

        //Assert
        ReferenceEquals(second.Buffer.Data, data).Should().BeTrue();
    }

    /// <summary>A buffer that is still being read is not handed out again.</summary>
    [Fact]
    public void Rent_does_not_hand_out_a_buffer_something_still_reads()
    {
        //Arrange
        var arena = new OnnxArena(true);
        var first = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 16 }, true);
        var alias = first.Reshaped(new long[] { 4, 4 });

        //Act
        first.Release(arena);
        var second = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 16 }, true);

        //Assert
        ReferenceEquals(second.Buffer.Data, alias.Buffer.Data).Should().BeFalse();
    }

    /// <summary>Once the last reader has let go, the buffer comes back.</summary>
    [Fact]
    public void Rent_hands_out_a_buffer_once_every_reader_has_let_go()
    {
        //Arrange
        var arena = new OnnxArena(true);
        var first = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 16 }, true);
        var alias = first.Reshaped(new long[] { 4, 4 });
        var data = first.Buffer.Data;

        //Act
        first.Release(arena);
        alias.Release(arena);
        var second = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 16 }, true);

        //Assert
        ReferenceEquals(second.Buffer.Data, data).Should().BeTrue();
    }

    /// <summary>A buffer a run is going to hand back never goes into the pool.</summary>
    [Fact]
    public void Return_keeps_an_output_buffer_out_of_the_pool()
    {
        //Arrange
        var arena = new OnnxArena(true);
        var value = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 8 }, false);

        //Act
        value.Release(arena);

        //Assert
        arena.PooledCount.Should().Be(0);
    }

    /// <summary>With reuse switched off nothing is ever kept.</summary>
    [Fact]
    public void Return_keeps_nothing_when_reuse_is_off()
    {
        //Arrange
        var arena = new OnnxArena(false);
        var value = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 8 }, true);

        //Act
        value.Release(arena);

        //Assert
        arena.PooledCount.Should().Be(0);
    }

    /// <summary>A request takes the smallest array that fits rather than the first one it finds.</summary>
    [Fact]
    public void Rent_takes_the_smallest_buffer_that_fits()
    {
        //Arrange
        var arena = new OnnxArena(true);
        var large = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 4096 }, true);
        var small = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 64 }, true);
        large.Release(arena);
        small.Release(arena);

        //Act
        var wanted = OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 32 }, true);

        //Assert
        wanted.Buffer.Capacity.Should().Be(64);
    }

    /// <summary>Each element type has its own pool.</summary>
    [Fact]
    public void Rent_keeps_the_element_types_apart()
    {
        //Arrange
        var arena = new OnnxArena(true);
        OnnxValue.Allocate(arena, OnnxElementType.Float, new long[] { 8 }, true).Release(arena);

        //Act
        var integers = OnnxValue.Allocate(arena, OnnxElementType.Int64, new long[] { 8 }, true);

        //Assert
        integers.Buffer.ElementType.Should().Be(OnnxElementType.Int64);
    }
}

using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The stream of random numbers a generation draws its choices from.
/// </summary>
/// <remarks>
/// It is written out in this library rather than taken from the framework because the framework's own
/// generator is allowed to change between releases, and a seed is meant to mean one thing for ever.
/// </remarks>
public sealed class GenerationRandomTests
{
    /// <summary>The same seed gives the same stream.</summary>
    [Fact]
    public void NextDouble_from_the_same_seed_gives_the_same_stream()
    {
        //Arrange
        GenerationRandom first = new GenerationRandom(20260918);
        GenerationRandom again = new GenerationRandom(20260918);

        //Act and assert
        for (int i = 0; i < 200; i++) again.NextDouble().Should().Be(first.NextDouble());
    }

    /// <summary>Different seeds give different streams.</summary>
    [Fact]
    public void NextDouble_from_different_seeds_gives_different_streams()
    {
        //Arrange
        GenerationRandom first = new GenerationRandom(1);
        GenerationRandom other = new GenerationRandom(2);
        int same = 0;

        //Act
        for (int i = 0; i < 50; i++)
        {
            if (first.NextDouble() == other.NextDouble()) same++;
        }

        //Assert
        same.Should().Be(0);
    }

    /// <summary>Every number is at least nought and below one.</summary>
    [Fact]
    public void NextDouble_is_at_least_nought_and_below_one()
    {
        //Arrange
        GenerationRandom random = new GenerationRandom(-5);

        //Act and assert
        for (int i = 0; i < 10000; i++)
        {
            double value = random.NextDouble();
            value.Should().BeGreaterThanOrEqualTo(0);
            value.Should().BeLessThan(1);
        }
    }

    /// <summary>A seed of nought is an ordinary seed and not a stuck one.</summary>
    [Fact]
    public void NextDouble_from_a_seed_of_nought_still_varies()
    {
        //Arrange
        GenerationRandom random = new GenerationRandom(0);
        HashSet<double> seen = new HashSet<double>();

        //Act
        for (int i = 0; i < 100; i++) seen.Add(random.NextDouble());

        //Assert
        seen.Should().HaveCount(100);
    }

    /// <summary>The numbers are spread across the range rather than gathered at one end.</summary>
    [Fact]
    public void NextDouble_spreads_across_the_range()
    {
        //Arrange
        GenerationRandom random = new GenerationRandom(77);
        int[] buckets = new int[10];

        //Act
        for (int i = 0; i < 100000; i++) buckets[(int)(random.NextDouble() * 10)]++;

        //Assert
        foreach (int count in buckets)
        {
            count.Should().BeGreaterThan(9000);
            count.Should().BeLessThan(11000);
        }
    }
}

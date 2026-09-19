using System;
using System.Collections.Generic;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The performance-core count the managed ONNX engine's thread default is built on, and the processor-list
/// format Linux states it in.
/// </summary>
/// <remarks>
/// What the count IS cannot be asserted here, because it is a property of whatever machine the suite runs on.
/// What can be asserted is that it is never nonsense - never negative, never more cores than the machine has
/// processors. The list parser is the one piece with a format of its own, and it is held to every shape the
/// kernel writes. What the count is USED for belongs to the engine that uses it: see
/// <c>OnnxExecutionSettingsTests</c>.
/// </remarks>
public sealed class EnginePerformanceCoresTests
{
    /// <summary>A single range, which is what an Intel-style hybrid machine writes.</summary>
    [Fact]
    public void ParseList_reads_a_range() =>
        EnginePerformanceCores.ParseList("0-15").Should().HaveCount(16);

    /// <summary>A range starts and ends where it says.</summary>
    [Fact]
    public void ParseList_reads_a_range_that_does_not_start_at_nought()
    {
        //Act
        HashSet<int> numbers = EnginePerformanceCores.ParseList("16-23");

        //Assert
        numbers.Should().HaveCount(8);
        numbers.Contains(16).Should().BeTrue();
        numbers.Contains(23).Should().BeTrue();
        numbers.Contains(15).Should().BeFalse();
        numbers.Contains(24).Should().BeFalse();
    }

    /// <summary>Single numbers separated by commas.</summary>
    [Fact]
    public void ParseList_reads_single_numbers()
    {
        //Act
        HashSet<int> numbers = EnginePerformanceCores.ParseList("0,2,4");

        //Assert
        numbers.Should().HaveCount(3);
        numbers.Contains(2).Should().BeTrue();
        numbers.Contains(1).Should().BeFalse();
    }

    /// <summary>Ranges and single numbers mixed, with the trailing newline the kernel writes.</summary>
    [Fact]
    public void ParseList_reads_ranges_and_numbers_together()
    {
        //Act
        HashSet<int> numbers = EnginePerformanceCores.ParseList("0-3,8-11,20\n");

        //Assert
        numbers.Should().HaveCount(9);
        numbers.Contains(20).Should().BeTrue();
        numbers.Contains(12).Should().BeFalse();
    }

    /// <summary>An empty or absent list is no processors rather than an error.</summary>
    /// <param name="text">What the file held.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public void ParseList_with_nothing_reads_no_processors(string text) =>
        EnginePerformanceCores.ParseList(text).Should().BeEmpty();

    /// <summary>Anything that is not a number is passed over rather than guessed at.</summary>
    [Fact]
    public void ParseList_passes_over_what_is_not_a_number()
    {
        //Act
        HashSet<int> numbers = EnginePerformanceCores.ParseList("0-1,rubbish,4,x-y");

        //Assert
        numbers.Should().HaveCount(3);
        numbers.Contains(4).Should().BeTrue();
    }

    /// <summary>The count is never negative and never more cores than the machine has processors.</summary>
    [Fact]
    public void Count_is_within_what_the_machine_has()
    {
        //Act
        int count = EnginePerformanceCores.Count();

        //Assert
        (count >= 0).Should().BeTrue();
        (count <= Environment.ProcessorCount).Should().BeTrue();
    }

    /// <summary>Asking twice gives the same answer, because the answer is worked out once and kept.</summary>
    [Fact]
    public void Count_answers_the_same_every_time() =>
        EnginePerformanceCores.Count().Should().Be(EnginePerformanceCores.Count());
}

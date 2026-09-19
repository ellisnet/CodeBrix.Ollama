using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The one rule both engines resolve a thread count by. The detected count is a parameter, so every case here
/// is about the RULE rather than about the processor the suite happens to be running on.
/// </summary>
public sealed class EngineThreadCountTests
{
    /// <summary>With nothing asked for and no cap, the detected count is the answer.</summary>
    [Fact]
    public void Resolve_with_nothing_asked_for_takes_the_detected_count() =>
        EngineThreadCount.Resolve(null, null, 16).Should().Be(16);

    /// <summary>A count the caller asked for is used exactly as it stands.</summary>
    [Fact]
    public void Resolve_uses_the_count_that_was_asked_for() =>
        EngineThreadCount.Resolve(3, null, 16).Should().Be(3);

    /// <summary>A count that was asked for wins even when it oversubscribes the machine.</summary>
    [Fact]
    public void Resolve_uses_a_count_that_was_asked_for_even_above_the_detected_one() =>
        EngineThreadCount.Resolve(64, null, 4).Should().Be(64);

    /// <summary>The cap has nothing to say about a count the caller asked for.</summary>
    [Theory]
    [InlineData(32, 4, 16, 32)]
    [InlineData(1, 8, 16, 1)]
    [InlineData(12, 2, 16, 12)]
    public void Resolve_ignores_the_cap_when_a_count_was_asked_for(
        int requested, int maximum, int detected, int expected) =>
        EngineThreadCount.Resolve(requested, maximum, detected).Should().Be(expected);

    /// <summary>The cap bounds the automatic count, which is the whole of what it is for.</summary>
    [Fact]
    public void Resolve_bounds_the_automatic_count_by_the_cap() =>
        EngineThreadCount.Resolve(null, 4, 16).Should().Be(4);

    /// <summary>
    /// A cap above what the machine has changes nothing: a four-core machine still gets four, which is the
    /// point of a cap rather than a count.
    /// </summary>
    [Fact]
    public void Resolve_with_a_cap_above_the_detected_count_changes_nothing() =>
        EngineThreadCount.Resolve(null, 64, 4).Should().Be(4);

    /// <summary>A cap equal to the detected count changes nothing either.</summary>
    [Fact]
    public void Resolve_with_a_cap_equal_to_the_detected_count_changes_nothing() =>
        EngineThreadCount.Resolve(null, 16, 16).Should().Be(16);

    /// <summary>A cap of one is a single thread, which is the smallest a cap can ask for.</summary>
    [Fact]
    public void Resolve_with_a_cap_of_one_gives_one_thread() =>
        EngineThreadCount.Resolve(null, 1, 16).Should().Be(1);

    /// <summary>The answer is never below one, whatever it was handed.</summary>
    [Theory]
    [InlineData(null, null, 0, 1)]
    [InlineData(null, null, -4, 1)]
    [InlineData(0, null, 16, 1)]
    [InlineData(-2, null, 16, 1)]
    public void Resolve_is_never_below_one(int? requested, int? maximum, int detected, int expected) =>
        EngineThreadCount.Resolve(requested, maximum, detected).Should().Be(expected);
}

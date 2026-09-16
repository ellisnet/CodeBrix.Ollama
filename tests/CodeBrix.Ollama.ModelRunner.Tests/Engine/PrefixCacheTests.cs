using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the record of what is in the context's memory, and the common-prefix rule that decides how much of
/// a new prompt the engine can skip.
/// </summary>
public sealed class PrefixCacheTests
{
    /// <summary>An empty cache lets nothing be reused.</summary>
    [Fact]
    public void ReusableLength_is_zero_for_an_empty_cache()
    {
        //Arrange
        PrefixCache cache = new PrefixCache();

        //Act
        int reusable = cache.ReusableLength(new[] { 1, 2, 3 });

        //Assert
        cache.Count.Should().Be(0);
        reusable.Should().Be(0);
    }

    /// <summary>A prompt that continues the last one reuses all of the shared history.</summary>
    [Fact]
    public void ReusableLength_counts_the_shared_prefix()
    {
        //Arrange
        PrefixCache cache = new PrefixCache();
        cache.AddRange(new[] { 1, 2, 3, 4 });

        //Act
        int reusable = cache.ReusableLength(new[] { 1, 2, 3, 9, 9 });

        //Assert
        reusable.Should().Be(3);
    }

    /// <summary>A prompt identical to what is cached still gives back its last token, so logits exist.</summary>
    [Fact]
    public void ReusableLength_always_leaves_the_last_prompt_token_to_evaluate()
    {
        //Arrange
        PrefixCache cache = new PrefixCache();
        cache.AddRange(new[] { 5, 6, 7 });

        //Act
        int reusable = cache.ReusableLength(new[] { 5, 6, 7 });

        //Assert
        reusable.Should().Be(2);
    }

    /// <summary>A prompt that shares nothing reuses nothing.</summary>
    [Fact]
    public void ReusableLength_is_zero_when_the_first_token_differs()
    {
        //Arrange
        PrefixCache cache = new PrefixCache();
        cache.AddRange(new[] { 1, 2, 3 });

        //Act
        int reusable = cache.ReusableLength(new[] { 9, 2, 3 });

        //Assert
        reusable.Should().Be(0);
    }

    /// <summary>A prompt shorter than the cache is capped by its own length.</summary>
    [Fact]
    public void ReusableLength_is_capped_by_the_shorter_of_the_two()
    {
        //Arrange
        PrefixCache cache = new PrefixCache();
        cache.AddRange(new[] { 1, 2, 3, 4, 5 });

        //Act
        int reusable = cache.ReusableLength(new[] { 1, 2 });

        //Assert
        reusable.Should().Be(1);
    }

    /// <summary>An empty prompt reuses nothing and does not fall over.</summary>
    [Fact]
    public void ReusableLength_is_zero_for_an_empty_prompt()
    {
        //Arrange
        PrefixCache cache = new PrefixCache();
        cache.AddRange(new[] { 1, 2, 3 });

        //Act
        int reusable = cache.ReusableLength(Array.Empty<int>());

        //Assert
        reusable.Should().Be(0);
    }

    /// <summary>Truncating drops the tail, matching a removal from the engine's memory.</summary>
    [Fact]
    public void TruncateTo_keeps_only_the_leading_tokens()
    {
        //Arrange
        PrefixCache cache = new PrefixCache();
        cache.AddRange(new[] { 1, 2, 3, 4 });

        //Act
        cache.TruncateTo(2);

        //Assert
        cache.Count.Should().Be(2);
        cache.ReusableLength(new[] { 1, 2, 3 }).Should().Be(2);
    }

    /// <summary>A truncation past the end is a programming error, not a silent no-op.</summary>
    [Fact]
    public void TruncateTo_refuses_a_length_the_cache_does_not_have()
    {
        //Arrange
        PrefixCache cache = new PrefixCache();
        cache.Add(1);

        //Act and assert
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.TruncateTo(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.TruncateTo(-1));
    }

    /// <summary>Clearing forgets everything, matching a cleared memory.</summary>
    [Fact]
    public void Clear_empties_the_record()
    {
        //Arrange
        PrefixCache cache = new PrefixCache();
        cache.AddRange(new[] { 1, 2, 3 });

        //Act
        cache.Clear();

        //Assert
        cache.Count.Should().Be(0);
        cache.ReusableLength(new[] { 1, 2, 3 }).Should().Be(0);
    }
}

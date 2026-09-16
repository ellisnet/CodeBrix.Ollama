using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the human-readable parameter-count formatting that goes into a model's config layer, against
/// the cases Ollama's own format package produces.
/// </summary>
public sealed class HumanFormatTests
{
    [Theory]
    [InlineData(0UL, "0")]
    [InlineData(1UL, "1")]
    [InlineData(999UL, "999")]
    [InlineData(1000UL, "1K")]
    [InlineData(1499UL, "1K")]
    [InlineData(1600UL, "2K")]
    [InlineData(512000UL, "512K")]
    [InlineData(999999UL, "1000K")]
    [InlineData(1000000UL, "1M")]
    [InlineData(134520000UL, "134.52M")]
    [InlineData(135000000UL, "135M")]
    [InlineData(999999999UL, "1000.00M")]
    [InlineData(1000000000UL, "1B")]
    [InlineData(1500000000UL, "1.5B")]
    [InlineData(7200000000UL, "7.2B")]
    [InlineData(8000000000UL, "8B")]
    [InlineData(70000000000UL, "70B")]
    public void HumanNumber_formats_the_count_like_ollama(ulong value, string expected)
    {
        HumanFormat.HumanNumber(value).Should().Be(expected);
    }
}

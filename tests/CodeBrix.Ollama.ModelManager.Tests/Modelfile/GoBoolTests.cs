using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Tests for <see cref="GoBool"/>, which accepts exactly the spellings Go's strconv.ParseBool does.
/// </summary>
public sealed class GoBoolTests
{
    /// <summary>Every spelling Go reads as true is accepted.</summary>
    /// <param name="value">The text to parse.</param>
    [Theory]
    [InlineData("1")]
    [InlineData("t")]
    [InlineData("T")]
    [InlineData("TRUE")]
    [InlineData("true")]
    [InlineData("True")]
    public void TryParse_WithTrueSpelling_ReturnsTrue(string value)
    {
        //Act
        var parsed = GoBool.TryParse(value, out var result);

        //Assert
        parsed.Should().BeTrue();
        result.Should().BeTrue();
    }

    /// <summary>Every spelling Go reads as false is accepted.</summary>
    /// <param name="value">The text to parse.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("f")]
    [InlineData("F")]
    [InlineData("FALSE")]
    [InlineData("false")]
    [InlineData("False")]
    public void TryParse_WithFalseSpelling_ReturnsFalse(string value)
    {
        //Act
        var parsed = GoBool.TryParse(value, out var result);

        //Assert
        parsed.Should().BeTrue();
        result.Should().BeFalse();
    }

    /// <summary>Spellings Go rejects are rejected here too.</summary>
    /// <param name="value">The text to parse.</param>
    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("TrUe")]
    [InlineData("tRUE")]
    [InlineData("FaLsE")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData(" true")]
    [InlineData("true ")]
    [InlineData("y")]
    [InlineData("n")]
    public void TryParse_WithUnknownSpelling_ReturnsFalse(string value)
    {
        //Act
        var parsed = GoBool.TryParse(value, out var result);

        //Assert
        parsed.Should().BeFalse();
        result.Should().BeFalse();
    }

    /// <summary>A null value is rejected rather than throwing.</summary>
    [Fact]
    public void TryParse_WithNull_ReturnsFalse()
    {
        //Act
        var parsed = GoBool.TryParse(null, out var result);

        //Assert
        parsed.Should().BeFalse();
        result.Should().BeFalse();
    }
}

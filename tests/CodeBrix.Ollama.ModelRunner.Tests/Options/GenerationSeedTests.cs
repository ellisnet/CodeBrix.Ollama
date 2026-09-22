using System;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>Randomness is requested with null; numeric seeds cannot silently mean random.</summary>
public sealed class GenerationSeedTests
{
    /// <summary>The unsigned representation of -1 is rejected by the options shared by both text runners.</summary>
    [Fact]
    public void SamplingOptions_rejects_the_native_random_sentinel()
    {
        //Arrange
        var options = new SamplingOptions { Seed = 1234 };
        int negative = -1;

        //Act
        Action assign = () => options.Seed = unchecked((uint)negative);

        //Assert
        var error = assign.Should().Throw<ArgumentOutOfRangeException>().Which;
        error.ParamName.Should().Be("Seed");
        error.Message.Should().Contain("Use null");
        options.Seed.Should().Be(1234);
    }

    /// <summary>MIDI rejects negative seeds and the same reserved numeric value as the text runners.</summary>
    /// <param name="seed">The invalid explicit seed.</param>
    [Theory]
    [InlineData(-1L)]
    [InlineData(-2L)]
    [InlineData(long.MinValue)]
    [InlineData(4294967295L)]
    public void MidiGenerationOptions_rejects_invalid_explicit_seeds(long seed)
    {
        //Arrange
        var options = new MidiGenerationOptions { Seed = 1234 };

        //Act
        Action assign = () => options.Seed = seed;

        //Assert
        var error = assign.Should().Throw<ArgumentOutOfRangeException>().Which;
        error.ParamName.Should().Be("Seed");
        error.Message.Should().Contain("Use null");
        options.Seed.Should().Be(1234);
    }

    /// <summary>Zero and the largest ordinary text seed remain fixed seeds on both option types.</summary>
    /// <param name="seed">An accepted seed.</param>
    [Theory]
    [InlineData(0u)]
    [InlineData(1234u)]
    [InlineData(uint.MaxValue - 1)]
    public void Options_preserve_valid_fixed_seeds(uint seed)
    {
        //Arrange and act
        var text = new SamplingOptions { Seed = seed };
        var midi = new MidiGenerationOptions { Seed = seed };
        var plan = CausalLmPlan.For(new GenerationOptions { Sampling = text });

        //Assert
        text.Seed.Should().Be(seed);
        midi.Copy().Seed.Should().Be(seed);
        plan.Seed.Should().Be(seed);
    }

    /// <summary>MIDI retains its nonnegative 64-bit seed range.</summary>
    [Fact]
    public void MidiGenerationOptions_preserves_large_fixed_seeds()
    {
        //Arrange and act
        var options = new MidiGenerationOptions { Seed = long.MaxValue };

        //Assert
        options.Copy().Seed.Should().Be(long.MaxValue);
    }

    /// <summary>Both option types default to random and can explicitly switch back to it after a fixed seed.</summary>
    [Fact]
    public void Options_allow_an_explicit_random_choice()
    {
        //Arrange
        var text = new SamplingOptions();
        var midi = new MidiGenerationOptions();
        text.Seed.Should().BeNull();
        midi.Seed.Should().BeNull();
        text.Seed = 1234;
        midi.Seed = 1234;

        //Act
        text.Seed = null;
        midi.Seed = null;
        var plan = CausalLmPlan.For(new GenerationOptions { Sampling = text });

        //Assert
        text.Seed.Should().BeNull();
        midi.Copy().Seed.Should().BeNull();
        plan.Sampling.Seed.Should().BeNull();
    }
}

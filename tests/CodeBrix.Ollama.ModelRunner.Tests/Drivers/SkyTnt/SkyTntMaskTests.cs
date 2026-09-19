using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The sampling masks, against the ones the publisher's own generation loop builds.
/// </summary>
/// <remarks>
/// EVERY ONE OF THEM MATTERS. A mask that is wrong does not fail: the model still answers, the row still
/// decodes, and the music is quietly not the music the publisher's code would have written. The fixtures were
/// computed once by running the publisher's own tokenizer through the branches of its own loop, so what these
/// tests compare against is not this port's idea of itself.
/// </remarks>
public sealed class SkyTntMaskTests
{
    /// <summary>The tiny bundle states the same vocabulary the fixtures were taken from.</summary>
    [Fact]
    public void the_fixtures_and_the_tokenizer_agree_about_the_vocabulary()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        SkyTntMaskFixture fixture = SkyTntFixtures.MaskFixture;

        //Assert
        tokenizer.VocabularySize.Should().Be(fixture.VocabSize);
        tokenizer.MaximumTokensPerEvent.Should().Be(fixture.MaxTokenSeq);
        tokenizer.PadId.Should().Be(fixture.PadId);
        tokenizer.BeginningId.Should().Be(fixture.BosId);
        tokenizer.EndId.Should().Be(fixture.EosId);
    }

    /// <summary>Each kind of event is introduced by the token the publisher's tokenizer allots it.</summary>
    [Fact]
    public void the_event_tokens_are_the_ones_the_publisher_allots()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();

        //Act and assert
        foreach (KeyValuePair<string, int> one in SkyTntFixtures.MaskFixture.EventIds)
        {
            tokenizer.TypeForName(one.Key).Should().NotBeNull();
            tokenizer.TypeForName(one.Key).Id.Should().Be(one.Value);
            tokenizer.TypeForToken(one.Value).Name.Should().Be(one.Key);
        }
    }

    /// <summary>Each parameter family owns the block of tokens the publisher's tokenizer allots it.</summary>
    [Fact]
    public void the_parameter_blocks_are_the_ones_the_publisher_allots()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        SkyTntMaskFixture fixture = SkyTntFixtures.MaskFixture;

        //Act and assert
        foreach (KeyValuePair<string, int> one in fixture.ParameterFirstIds)
        {
            SkyTntParameter parameter = tokenizer.Parameter(one.Key);
            parameter.FirstId.Should().Be(one.Value);
            parameter.Size.Should().Be(fixture.ParameterSizes[one.Key]);
        }
    }

    /// <summary>Every mask is exactly the publisher's.</summary>
    /// <param name="name">Which mask.</param>
    [Theory]
    [MemberData(nameof(Masks))]
    public void Allowed_is_the_publishers_own_mask(string name)
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        SkyTntMaskCase expected = SkyTntFixtures.Mask(name);
        bool[] channels = Channels(expected.DisabledChannels);

        //Act
        bool[] allowed = SkyTntMask.Allowed(
            tokenizer,
            expected.Position,
            expected.Ended,
            expected.Event == null ? null : tokenizer.TypeForName(expected.Event),
            expected.DisablePatchChange,
            expected.DisableControlChange,
            channels);

        //Assert
        List<int> tokens = new List<int>();
        for (int i = 0; i < allowed.Length; i++)
        {
            if (allowed[i]) tokens.Add(i);
        }

        allowed.Length.Should().Be(tokenizer.VocabularySize);
        tokens.Should().HaveCount(expected.Allowed.Count);
        for (int i = 0; i < tokens.Count; i++) tokens[i].Should().Be(expected.Allowed[i]);
    }

    /// <summary>Every mask's name, as a theory's data.</summary>
    /// <returns>One row per mask.</returns>
    public static TheoryData<string> Masks() => SkyTntFixtures.AllMasks();

    private static bool[] Channels(IReadOnlyList<int> disabled)
    {
        bool[] channels = new bool[16];
        for (int i = 0; i < channels.Length; i++) channels[i] = true;
        if (disabled == null) return channels;

        foreach (int channel in disabled) channels[channel] = false;
        return channels;
    }
}

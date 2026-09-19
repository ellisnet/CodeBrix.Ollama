using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// What a generation starts from: the caller's settings turned into the rows of tokens the model reads first,
/// and the refusals that go with them.
/// </summary>
public sealed class SkyTntPromptTests
{
    /// <summary>With nothing asked for, the model is told only that a piece of music follows.</summary>
    [Fact]
    public void Plan_with_nothing_asked_for_starts_with_the_beginning_token_alone()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();

        //Act
        SkyTntGenerationPlan plan = SkyTntPrompt.Plan(tokenizer, new MidiGenerationOptions());

        //Assert
        plan.PromptRows.Should().HaveCount(1);
        plan.PromptRows[0][0].Should().Be(tokenizer.BeginningId);
        plan.DisableProgramChange.Should().BeFalse();
        plan.DisableControlChange.Should().BeFalse();
        foreach (bool channel in plan.AllowedChannels) channel.Should().BeTrue();
    }

    /// <summary>The settings become events in the order the publisher's own page writes them.</summary>
    [Fact]
    public void Plan_writes_the_settings_in_the_publishers_own_order()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiGenerationOptions options = new MidiGenerationOptions
        {
            TimeSignatureNumerator = 3,
            TimeSignatureDenominator = 4,
            KeySignatureSharpsOrFlats = -3,
            KeySignatureIsMinor = true,
            BeatsPerMinute = 100,
            Instruments = new[] { 0, 40, 73 },
            DrumKit = 0,
        };

        //Act
        SkyTntGenerationPlan plan = SkyTntPrompt.Plan(tokenizer, options);

        //Assert
        plan.PromptRows.Should().HaveCount(8);
        Describe(tokenizer, plan.PromptRows[1]).Should().Be("time_signature 0 0 0 2 1");
        Describe(tokenizer, plan.PromptRows[2]).Should().Be("key_signature 0 0 0 4 1");
        Describe(tokenizer, plan.PromptRows[3]).Should().Be("set_tempo 0 0 0 100");
        Describe(tokenizer, plan.PromptRows[4]).Should().Be("patch_change 0 0 1 0 0");
        Describe(tokenizer, plan.PromptRows[5]).Should().Be("patch_change 0 0 2 1 40");
        Describe(tokenizer, plan.PromptRows[6]).Should().Be("patch_change 0 0 3 2 73");
        Describe(tokenizer, plan.PromptRows[7]).Should().Be("patch_change 0 0 4 9 0");
    }

    /// <summary>Naming instruments also stops the model choosing its own or straying off their channels.</summary>
    [Fact]
    public void Plan_with_instruments_closes_the_channels_that_were_not_asked_for()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiGenerationOptions options = new MidiGenerationOptions
        {
            Instruments = new[] { 0, 40, 73 },
            DrumKit = 0,
        };

        //Act
        SkyTntGenerationPlan plan = SkyTntPrompt.Plan(tokenizer, options);

        //Assert
        plan.DisableProgramChange.Should().BeTrue();
        for (int channel = 0; channel < 16; channel++)
        {
            bool expected = channel == 0 || channel == 1 || channel == 2 || channel == 9;
            plan.AllowedChannels[channel].Should().Be(expected);
        }
    }

    /// <summary>The ninth instrument steps over the percussion channel.</summary>
    [Fact]
    public void Plan_steps_over_the_percussion_channel_when_handing_channels_out()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        int[] instruments = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        MidiGenerationOptions options = new MidiGenerationOptions { Instruments = instruments };

        //Act
        SkyTntGenerationPlan plan = SkyTntPrompt.Plan(tokenizer, options);

        //Assert
        plan.AllowedChannels[8].Should().BeTrue();
        plan.AllowedChannels[9].Should().BeFalse();
        plan.AllowedChannels[10].Should().BeTrue();
    }

    /// <summary>A drum kit alone leaves the model free to choose the rest.</summary>
    [Fact]
    public void Plan_with_a_drum_kit_alone_leaves_the_model_free()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();

        //Act
        SkyTntGenerationPlan plan = SkyTntPrompt.Plan(
            tokenizer, new MidiGenerationOptions { DrumKit = 8 });

        //Assert
        plan.DisableProgramChange.Should().BeFalse();
        foreach (bool channel in plan.AllowedChannels) channel.Should().BeTrue();
    }

    /// <summary>Refusing controller changes is passed straight through to the mask.</summary>
    [Fact]
    public void Plan_passes_a_refusal_of_controller_changes_through()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();

        //Act
        SkyTntGenerationPlan plan = SkyTntPrompt.Plan(
            tokenizer, new MidiGenerationOptions { AllowControlChange = false });

        //Assert
        plan.DisableControlChange.Should().BeTrue();
    }

    /// <summary>A piece to continue becomes the rows the model reads, without the token that ends a piece.</summary>
    [Fact]
    public void Plan_with_a_piece_to_continue_takes_the_ending_token_off_it()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore prompt = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 240),
            MidiEvent.Note(480, 0, 0, 64, 100, 240),
        });

        //Act
        SkyTntGenerationPlan plan = SkyTntPrompt.Plan(
            tokenizer, new MidiGenerationOptions { Prompt = prompt });

        //Assert
        plan.PromptRows[0][0].Should().Be(tokenizer.BeginningId);
        foreach (int[] row in plan.PromptRows) row[0].Should().NotBe(tokenizer.EndId);
    }

    /// <summary>Only the first part of a long piece is read, when the caller says so.</summary>
    [Fact]
    public void Plan_keeps_only_as_much_of_a_piece_as_it_was_told_to()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        List<MidiEvent> events = new List<MidiEvent>();
        for (int i = 0; i < 40; i++) events.Add(MidiEvent.Note(i * 240, 0, 0, 60 + (i % 12), 100, 120));
        MidiScore prompt = new MidiScore(480, events);

        //Act
        SkyTntGenerationPlan plan = SkyTntPrompt.Plan(
            tokenizer, new MidiGenerationOptions { Prompt = prompt, PromptEventLimit = 6 });

        //Assert
        plan.PromptRows.Should().HaveCount(6);
    }

    /// <summary>A piece to continue and a description of one to start are refused together.</summary>
    [Fact]
    public void Plan_refuses_a_piece_to_continue_together_with_a_piece_to_start()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiGenerationOptions options = new MidiGenerationOptions
        {
            Prompt = new MidiScore(480, new[] { MidiEvent.Note(0, 0, 0, 60, 100, 240) }),
            BeatsPerMinute = 120,
        };
        Action act = () => SkyTntPrompt.Plan(tokenizer, options);

        //Act and assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("alternatives");
    }

    /// <summary>Every option outside the range stated for it is refused, saying which.</summary>
    /// <param name="change">What to set wrongly.</param>
    /// <param name="expected">A word the refusal has to carry.</param>
    [Theory]
    [InlineData("events", "at least one event")]
    [InlineData("temperature", "above nought")]
    [InlineData("topP", "at most one")]
    [InlineData("topK", "At least one token")]
    [InlineData("bpm", "quarter notes per minute")]
    [InlineData("numerator", "upper number")]
    [InlineData("denominator", "lower number")]
    [InlineData("halfSignature", "both or neither")]
    [InlineData("key", "sharps or flats")]
    [InlineData("instrument", "program number")]
    [InlineData("drumKit", "program number")]
    [InlineData("promptLimit", "At least one event of a prompt")]
    public void Plan_refuses_an_option_outside_its_range(string change, string expected)
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiGenerationOptions options = new MidiGenerationOptions();
        switch (change)
        {
            case "events": options.MaximumEvents = 0; break;
            case "temperature": options.Temperature = 0; break;
            case "topP": options.TopP = 1.5; break;
            case "topK": options.TopK = 0; break;
            case "bpm": options.BeatsPerMinute = 400; break;
            case "numerator":
                options.TimeSignatureNumerator = 17;
                options.TimeSignatureDenominator = 4;
                break;
            case "denominator":
                options.TimeSignatureNumerator = 4;
                options.TimeSignatureDenominator = 3;
                break;
            case "halfSignature": options.TimeSignatureNumerator = 4; break;
            case "key": options.KeySignatureSharpsOrFlats = 8; break;
            case "instrument": options.Instruments = new[] { 200 }; break;
            case "drumKit": options.DrumKit = -1; break;
            case "promptLimit": options.PromptEventLimit = 0; break;
        }

        Action act = () => SkyTntPrompt.Plan(tokenizer, options);

        //Act and assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain(expected);
    }

    /// <summary>Without a seed of its own a generation still gets one.</summary>
    [Fact]
    public void Plan_without_a_seed_takes_one_from_the_clock()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();

        //Act
        SkyTntGenerationPlan plan = SkyTntPrompt.Plan(tokenizer, new MidiGenerationOptions());

        //Assert
        plan.Seed.Should().NotBe(0);
    }

    /// <summary>A seed the caller chose is the one that is used.</summary>
    [Fact]
    public void Plan_keeps_the_seed_the_caller_chose() =>
        SkyTntPrompt.Plan(SkyTntFixtures.Tokenizer(), new MidiGenerationOptions { Seed = 4242 })
            .Seed.Should().Be(4242);

    private static string Describe(SkyTntTokenizer tokenizer, int[] row)
    {
        SkyTntEventRow decoded = tokenizer.TokensToEvent(row);
        string text = decoded.Type.Name;
        foreach (int value in decoded.Values) text += " " + value;
        return text;
    }
}

using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The model's own tokenizer: the vocabulary, the two directions between an event and its row of tokens, and
/// the way a whole generation is turned back into a piece of music.
/// </summary>
public sealed class SkyTntTokenizerTests
{
    /// <summary>The vocabulary is the one the published bundle describes.</summary>
    [Fact]
    public void FromConfiguration_builds_the_vocabulary_the_bundle_describes()
    {
        //Arrange and act
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();

        //Assert
        tokenizer.VocabularySize.Should().Be(3406);
        tokenizer.MaximumTokensPerEvent.Should().Be(8);
        tokenizer.PadId.Should().Be(0);
        tokenizer.BeginningId.Should().Be(1);
        tokenizer.EndId.Should().Be(2);
        tokenizer.EventTypes.Should().HaveCount(6);
        tokenizer.OptimiseMidi.Should().BeTrue();
    }

    /// <summary>A bundle asking for a tokenizer this driver does not implement is refused by name.</summary>
    [Fact]
    public void FromConfiguration_of_another_version_is_refused()
    {
        //Arrange
        SkyTntTokenizerConfiguration configuration = Configuration("v1");
        Action act = () => SkyTntTokenizer.FromConfiguration(configuration);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("v1");
    }

    /// <summary>A bundle whose numbers do not match the layout implemented here is refused.</summary>
    [Fact]
    public void FromConfiguration_of_a_different_vocabulary_is_refused()
    {
        //Arrange
        SkyTntTokenizerConfiguration real = SkyTntFixtures.Configuration();
        SkyTntTokenizerConfiguration wrong = new SkyTntTokenizerConfiguration(
            real.Version, real.OptimiseMidi, real.VocabularySize + 1, real.MaximumTokensPerEvent,
            real.PadId, real.BeginningId, real.EndId, real.Events, real.EventParameters);
        Action act = () => SkyTntTokenizer.FromConfiguration(wrong);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("vocabulary size");
    }

    /// <summary>A bundle whose events name a parameter this driver does not put there is refused.</summary>
    [Fact]
    public void FromConfiguration_of_a_different_event_is_refused()
    {
        //Arrange
        SkyTntTokenizerConfiguration real = SkyTntFixtures.Configuration();
        Dictionary<string, IReadOnlyList<string>> events =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IReadOnlyList<string>> one in real.Events) events[one.Key] = one.Value;
        events["note"] = new[] { "time1", "time2", "track", "channel", "pitch", "velocity", "bpm" };

        SkyTntTokenizerConfiguration wrong = new SkyTntTokenizerConfiguration(
            real.Version, real.OptimiseMidi, real.VocabularySize, real.MaximumTokensPerEvent, real.PadId,
            real.BeginningId, real.EndId, events, real.EventParameters);
        Action act = () => SkyTntTokenizer.FromConfiguration(wrong);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("duration");
    }

    /// <summary>An event becomes a row of tokens and comes back unchanged.</summary>
    /// <param name="name">The kind of event.</param>
    /// <param name="values">Its parameters.</param>
    [Theory]
    [InlineData("note", new[] { 3, 5, 2, 9, 60, 100, 48 })]
    [InlineData("note", new[] { 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData("note", new[] { 127, 15, 127, 15, 127, 127, 2047 })]
    [InlineData("patch_change", new[] { 1, 2, 3, 4, 5 })]
    [InlineData("control_change", new[] { 0, 8, 1, 2, 7, 127 })]
    [InlineData("set_tempo", new[] { 4, 0, 0, 120 })]
    [InlineData("time_signature", new[] { 0, 0, 0, 3, 1 })]
    [InlineData("key_signature", new[] { 0, 0, 1, 12, 0 })]
    public void EventToTokens_and_TokensToEvent_are_each_others_undoing(string name, int[] values)
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        SkyTntEventRow row = new SkyTntEventRow(tokenizer.TypeForName(name), values);

        //Act
        int[] tokens = tokenizer.EventToTokens(row);
        SkyTntEventRow back = tokenizer.TokensToEvent(tokens);

        //Assert
        tokens.Should().HaveCount(8);
        tokens[0].Should().Be(tokenizer.TypeForName(name).Id);
        back.Should().NotBeNull();
        back.Type.Name.Should().Be(name);
        back.Values.Should().Equal(values);
    }

    /// <summary>The unused end of a row is padding.</summary>
    [Fact]
    public void EventToTokens_pads_the_unused_end_of_a_row()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        SkyTntEventRow row = new SkyTntEventRow(tokenizer.TypeForName("set_tempo"), new[] { 0, 0, 0, 120 });

        //Act
        int[] tokens = tokenizer.EventToTokens(row);

        //Assert
        tokens[5].Should().Be(tokenizer.PadId);
        tokens[6].Should().Be(tokenizer.PadId);
        tokens[7].Should().Be(tokenizer.PadId);
    }

    /// <summary>An event a parameter cannot express is dropped rather than written wrong.</summary>
    [Fact]
    public void EventToTokens_of_a_value_out_of_range_gives_nothing()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        SkyTntEventRow row = new SkyTntEventRow(
            tokenizer.TypeForName("set_tempo"), new[] { 0, 0, 0, 384 });

        //Act
        int[] tokens = tokenizer.EventToTokens(row);

        //Assert
        tokens.Should().BeNull();
    }

    /// <summary>A row that does not begin an event is not one.</summary>
    /// <param name="first">The token the row begins with.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3405)]
    public void TokensToEvent_of_a_row_that_begins_no_event_gives_nothing(int first)
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        int[] row = { first, 0, 0, 0, 0, 0, 0, 0 };

        //Act and assert
        tokenizer.TokensToEvent(row).Should().BeNull();
    }

    /// <summary>A row whose parameter is from the wrong family is not an event.</summary>
    [Fact]
    public void TokensToEvent_of_a_token_from_the_wrong_family_gives_nothing()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        int[] row = { tokenizer.TypeForName("note").Id, 0, 0, 0, 0, 0, 0, 0 };

        //Act and assert
        tokenizer.TokensToEvent(row).Should().BeNull();
    }

    /// <summary>A row too short for the kind it begins is not an event.</summary>
    [Fact]
    public void TokensToEvent_of_too_short_a_row_gives_nothing()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        SkyTntEventRow row = new SkyTntEventRow(
            tokenizer.TypeForName("note"), new[] { 0, 0, 0, 0, 60, 100, 8 });
        int[] tokens = tokenizer.EventToTokens(row);
        int[] cut = new int[4];
        Array.Copy(tokens, cut, 4);

        //Act and assert
        tokenizer.TokensToEvent(cut).Should().BeNull();
    }

    /// <summary>A beat and an offset inside it become the position the model means.</summary>
    /// <param name="beat">The beat.</param>
    /// <param name="withinBeat">The offset inside it, in sixteenths.</param>
    /// <param name="expected">The position in ticks.</param>
    [Theory]
    [InlineData(0, 0, 0L)]
    [InlineData(0, 1, 30L)]
    [InlineData(0, 15, 450L)]
    [InlineData(1, 0, 480L)]
    [InlineData(4, 8, 2160L)]
    public void TickOf_places_an_event_where_the_model_means(int beat, int withinBeat, long expected) =>
        SkyTntTokenizer.TickOf(beat, withinBeat).Should().Be(expected);

    /// <summary>A speed in beats a minute becomes the microseconds a file holds.</summary>
    /// <param name="beatsPerMinute">The speed.</param>
    /// <param name="expected">The microseconds a quarter note lasts.</param>
    [Theory]
    [InlineData(120, 500000L)]
    [InlineData(60, 1000000L)]
    [InlineData(100, 600000L)]
    [InlineData(383, 156657L)]
    [InlineData(0, 60000000L)]
    public void MicrosecondsFor_is_the_publishers_own_arithmetic(int beatsPerMinute, long expected) =>
        SkyTntTokenizer.MicrosecondsFor(beatsPerMinute).Should().Be(expected);

    /// <summary>A generation becomes a piece of music at the model's own resolution.</summary>
    [Fact]
    public void Detokenize_turns_rows_of_tokens_into_a_piece()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        List<int[]> rows = new List<int[]>
        {
            tokenizer.BeginningRow(),
            Row(tokenizer, "time_signature", 0, 0, 0, 3, 1),
            Row(tokenizer, "set_tempo", 0, 0, 0, 120),
            Row(tokenizer, "note", 0, 0, 1, 0, 60, 100, 8),
            Row(tokenizer, "note", 1, 8, 1, 0, 64, 100, 8),
            tokenizer.EndRow(),
        };

        //Act
        MidiScore score = tokenizer.Detokenize(rows);

        //Assert
        score.TicksPerQuarterNote.Should().Be(480);
        score.Events.Should().HaveCount(4);
        score.Events[0].Kind.Should().Be(MidiEventKind.TimeSignature);
        score.Events[0].Numerator.Should().Be(4);
        score.Events[0].Denominator.Should().Be(4);
        score.Events[1].Kind.Should().Be(MidiEventKind.Tempo);
        score.Events[1].BeatsPerMinute.Should().BeApproximately(120, 0.001);
        score.Events[2].Tick.Should().Be(0);
        score.Events[2].DurationTicks.Should().Be(240);
        score.Events[3].Tick.Should().Be(720);
    }

    /// <summary>A note the next one of its pitch cuts off is shortened to where that one begins.</summary>
    [Fact]
    public void Detokenize_shortens_a_note_the_next_one_of_its_pitch_cuts_off()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        List<int[]> rows = new List<int[]>
        {
            tokenizer.BeginningRow(),
            Row(tokenizer, "note", 0, 0, 1, 0, 60, 100, 64),   // four beats long
            Row(tokenizer, "note", 1, 0, 1, 0, 60, 100, 16),   // but the same pitch begins a beat later
        };

        //Act
        MidiScore score = tokenizer.Detokenize(rows);

        //Assert
        score.Events.Should().HaveCount(2);
        score.Events[0].DurationTicks.Should().Be(480);
        score.Events[1].DurationTicks.Should().Be(480);
    }

    /// <summary>A note left with no length at all is dropped, because it can never be heard.</summary>
    [Fact]
    public void Detokenize_drops_a_note_left_with_no_length()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        List<int[]> rows = new List<int[]>
        {
            tokenizer.BeginningRow(),
            Row(tokenizer, "note", 0, 0, 1, 0, 60, 100, 16),
            Row(tokenizer, "note", 0, 0, 1, 0, 60, 100, 16),   // at the very same position
        };

        //Act
        MidiScore score = tokenizer.Detokenize(rows);

        //Assert
        score.Events.Should().HaveCount(1);
    }

    /// <summary>A note of no length at all in the first place never becomes an event.</summary>
    [Fact]
    public void Detokenize_drops_a_note_of_no_length_in_the_first_place()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        List<int[]> rows = new List<int[]>
        {
            tokenizer.BeginningRow(),
            Row(tokenizer, "note", 0, 0, 1, 0, 60, 100, 0),
        };

        //Act and assert
        tokenizer.Detokenize(rows).Events.Should().BeEmpty();
    }

    /// <summary>The distances between events add up into positions from the start of the piece.</summary>
    [Fact]
    public void Detokenize_adds_the_distances_between_events_up()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        List<int[]> rows = new List<int[]>
        {
            tokenizer.BeginningRow(),
            Row(tokenizer, "note", 2, 0, 1, 0, 60, 100, 8),
            Row(tokenizer, "note", 3, 4, 1, 0, 62, 100, 8),
            Row(tokenizer, "note", 0, 12, 1, 0, 64, 100, 8),
        };

        //Act
        MidiScore score = tokenizer.Detokenize(rows);

        //Assert
        score.Events[0].Tick.Should().Be(960);
        score.Events[1].Tick.Should().Be(2520);
        score.Events[2].Tick.Should().Be(2760);
    }

    private static int[] Row(SkyTntTokenizer tokenizer, string name, params int[] values) =>
        tokenizer.EventToTokens(new SkyTntEventRow(tokenizer.TypeForName(name), values));

    private static SkyTntTokenizerConfiguration Configuration(string version)
    {
        SkyTntTokenizerConfiguration real = SkyTntFixtures.Configuration();
        return new SkyTntTokenizerConfiguration(
            version, real.OptimiseMidi, real.VocabularySize, real.MaximumTokensPerEvent, real.PadId,
            real.BeginningId, real.EndId, real.Events, real.EventParameters);
    }
}

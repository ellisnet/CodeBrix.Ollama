using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// A whole piece of music: how it is ordered, how long it is, and what it refuses.
/// </summary>
public sealed class MidiScoreTests
{
    /// <summary>The events are put in order of position.</summary>
    [Fact]
    public void the_events_are_put_in_order_of_position()
    {
        //Arrange and act
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(960, 0, 0, 64, 100, 120),
            MidiEvent.Note(0, 0, 0, 60, 100, 120),
            MidiEvent.Note(480, 0, 0, 62, 100, 120),
        });

        //Assert
        score.Events[0].Tick.Should().Be(0);
        score.Events[1].Tick.Should().Be(480);
        score.Events[2].Tick.Should().Be(960);
    }

    /// <summary>At one position, what sets something up comes before what sounds under it.</summary>
    [Fact]
    public void at_one_position_what_sets_something_up_comes_first()
    {
        //Arrange and act
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 120),
            MidiEvent.ProgramChange(0, 0, 0, 24),
            MidiEvent.Tempo(0, 0, 90),
        });

        //Assert
        score.Events[0].Kind.Should().Be(MidiEventKind.ProgramChange);
        score.Events[1].Kind.Should().Be(MidiEventKind.Tempo);
        score.Events[2].Kind.Should().Be(MidiEventKind.Note);
    }

    /// <summary>Two events at one position in one phase stay in the order they were given.</summary>
    [Fact]
    public void two_events_at_one_position_stay_in_the_order_they_were_given()
    {
        //Arrange and act
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 67, 100, 120),
            MidiEvent.Note(0, 0, 0, 60, 100, 120),
            MidiEvent.Note(0, 0, 0, 64, 100, 120),
        });

        //Assert
        score.Events[0].NoteNumber.Should().Be(67);
        score.Events[1].NoteNumber.Should().Be(60);
        score.Events[2].NoteNumber.Should().Be(64);
    }

    /// <summary>A piece is as many tracks as its highest-numbered one, counted from nought.</summary>
    [Fact]
    public void TrackCount_counts_from_nought()
    {
        //Arrange and act
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 120),
            MidiEvent.Note(0, 3, 1, 64, 100, 120),
        });

        //Assert
        score.TrackCount.Should().Be(4);
    }

    /// <summary>A piece ends where its last note stops sounding.</summary>
    [Fact]
    public void LengthInTicks_is_where_the_last_note_stops_sounding()
    {
        //Arrange and act
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 1920),
            MidiEvent.Note(480, 0, 0, 64, 100, 120),
        });

        //Assert
        score.LengthInTicks.Should().Be(1920);
    }

    /// <summary>Its length in seconds follows its own tempo from beginning to end.</summary>
    [Fact]
    public void DurationInSeconds_follows_the_pieces_own_tempo()
    {
        //Arrange
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Tempo(0, 0, 120),
            MidiEvent.Note(0, 0, 0, 60, 100, 960),
            MidiEvent.Tempo(960, 0, 60),
            MidiEvent.Note(960, 0, 0, 64, 100, 960),
        });

        //Act and assert
        score.DurationInSeconds().Should().BeApproximately(3.0, 0.0001);
    }

    /// <summary>With nothing said about the speed it runs at a hundred and twenty beats a minute.</summary>
    [Fact]
    public void DurationInSeconds_of_a_piece_that_says_nothing_about_its_speed()
    {
        //Arrange
        MidiScore score = new MidiScore(480, new[] { MidiEvent.Note(0, 0, 0, 60, 100, 960) });

        //Act and assert
        score.DurationInSeconds().Should().BeApproximately(1.0, 0.0001);
    }

    /// <summary>A piece with nothing in it is empty rather than an error.</summary>
    [Fact]
    public void a_piece_with_nothing_in_it_is_empty()
    {
        //Arrange and act
        MidiScore score = new MidiScore(480, Array.Empty<MidiEvent>());

        //Assert
        score.Events.Should().BeEmpty();
        score.TrackCount.Should().Be(0);
        score.LengthInTicks.Should().Be(0);
        score.DurationInSeconds().Should().Be(0);
    }

    /// <summary>A resolution a file cannot hold is refused.</summary>
    /// <param name="ticksPerQuarterNote">The resolution.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(32768)]
    public void a_resolution_a_file_cannot_hold_is_refused(int ticksPerQuarterNote)
    {
        //Arrange
        Action act = () => new MidiScore(ticksPerQuarterNote, Array.Empty<MidiEvent>());

        //Act and assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>A piece cannot be built from nothing, or hold an event that is not there.</summary>
    [Fact]
    public void a_piece_cannot_hold_an_event_that_is_not_there()
    {
        //Arrange
        Action fromNothing = () => new MidiScore(480, null);
        Action withAGap = () => new MidiScore(480, new MidiEvent[] { null });

        //Act and assert
        fromNothing.Should().Throw<ArgumentNullException>();
        withAGap.Should().Throw<ArgumentException>();
    }

    /// <summary>The caller's own collection is left alone.</summary>
    [Fact]
    public void the_callers_own_collection_is_left_alone()
    {
        //Arrange
        System.Collections.Generic.List<MidiEvent> events = new System.Collections.Generic.List<MidiEvent>
        {
            MidiEvent.Note(960, 0, 0, 64, 100, 120),
            MidiEvent.Note(0, 0, 0, 60, 100, 120),
        };

        //Act
        MidiScore score = new MidiScore(480, events);
        events.Clear();

        //Assert
        score.Events.Should().HaveCount(2);
    }
}

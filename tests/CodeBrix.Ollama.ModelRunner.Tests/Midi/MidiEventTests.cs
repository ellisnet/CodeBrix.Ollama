using System;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// One musical event: what each kind carries, and what is refused.
/// </summary>
public sealed class MidiEventTests
{
    /// <summary>A note carries its pitch, its loudness and its own length.</summary>
    [Fact]
    public void Note_carries_its_pitch_its_loudness_and_its_length()
    {
        //Arrange and act
        MidiEvent item = MidiEvent.Note(480, 2, 9, 36, 110, 120);

        //Assert
        item.Kind.Should().Be(MidiEventKind.Note);
        item.Tick.Should().Be(480);
        item.HorizonTicks.Should().Be(480);
        item.Track.Should().Be(2);
        item.Channel.Should().Be(9);
        item.NoteNumber.Should().Be(36);
        item.Velocity.Should().Be(110);
        item.DurationTicks.Should().Be(120);
    }

    /// <summary>An event of a kind that belongs to no channel says so.</summary>
    /// <param name="kind">Which kind.</param>
    [Theory]
    [InlineData("tempo")]
    [InlineData("time")]
    [InlineData("key")]
    public void an_event_of_no_channel_says_so(string kind)
    {
        //Arrange
        MidiEvent item = kind switch
        {
            "tempo" => MidiEvent.Tempo(0, 0, 120),
            "time" => MidiEvent.TimeSignature(0, 0, 4, 4),
            _ => MidiEvent.KeySignature(0, 0, 0, false),
        };

        //Act and assert
        item.Channel.Should().Be(MidiEvent.NoChannel);
    }

    /// <summary>A speed in beats a minute and the microseconds a file holds say the same thing.</summary>
    /// <param name="beatsPerMinute">The speed.</param>
    /// <param name="microseconds">What a file holds for it.</param>
    [Theory]
    [InlineData(120.0, 500000L)]
    [InlineData(60.0, 1000000L)]
    [InlineData(240.0, 250000L)]
    public void Tempo_and_TempoFromMicroseconds_say_the_same_thing(
        double beatsPerMinute, long microseconds)
    {
        //Arrange and act
        MidiEvent fromBeats = MidiEvent.Tempo(0, 0, beatsPerMinute);
        MidiEvent fromMicroseconds = MidiEvent.TempoFromMicroseconds(0, 0, microseconds);

        //Assert
        fromBeats.MicrosecondsPerQuarterNote.Should().Be(microseconds);
        fromMicroseconds.BeatsPerMinute.Should().BeApproximately(beatsPerMinute, 0.0001);
    }

    /// <summary>A time signature carries the two numbers it is written with.</summary>
    [Fact]
    public void TimeSignature_carries_the_two_numbers_it_is_written_with()
    {
        //Arrange and act
        MidiEvent item = MidiEvent.TimeSignature(0, 0, 7, 8);

        //Assert
        item.Numerator.Should().Be(7);
        item.Denominator.Should().Be(8);
    }

    /// <summary>A key signature carries its sharps or flats and whether it is minor.</summary>
    [Fact]
    public void KeySignature_carries_its_sharps_or_flats()
    {
        //Arrange and act
        MidiEvent item = MidiEvent.KeySignature(0, 0, -4, true);

        //Assert
        item.SharpsOrFlats.Should().Be(-4);
        item.IsMinor.Should().BeTrue();
    }

    /// <summary>Every value outside the range stated for it is refused.</summary>
    /// <param name="which">Which value to set wrongly.</param>
    [Theory]
    [InlineData("tick")]
    [InlineData("track")]
    [InlineData("channel")]
    [InlineData("note")]
    [InlineData("velocity")]
    [InlineData("duration")]
    [InlineData("program")]
    [InlineData("controller")]
    [InlineData("value")]
    [InlineData("bpm")]
    [InlineData("microseconds")]
    [InlineData("numerator")]
    [InlineData("denominator")]
    [InlineData("key")]
    public void a_value_outside_its_range_is_refused(string which)
    {
        //Arrange
        Action act = which switch
        {
            "tick" => () => MidiEvent.Note(-1, 0, 0, 60, 100, 10),
            "track" => () => MidiEvent.Note(0, -1, 0, 60, 100, 10),
            "channel" => () => MidiEvent.Note(0, 0, 16, 60, 100, 10),
            "note" => () => MidiEvent.Note(0, 0, 0, 128, 100, 10),
            "velocity" => () => MidiEvent.Note(0, 0, 0, 60, 128, 10),
            "duration" => () => MidiEvent.Note(0, 0, 0, 60, 100, 0),
            "program" => () => MidiEvent.ProgramChange(0, 0, 0, 128),
            "controller" => () => MidiEvent.ControlChange(0, 0, 0, 128, 0),
            "value" => () => MidiEvent.ControlChange(0, 0, 0, 7, 128),
            "bpm" => () => MidiEvent.Tempo(0, 0, 0),
            "microseconds" => () => MidiEvent.TempoFromMicroseconds(0, 0, 0x1000000),
            "numerator" => () => MidiEvent.TimeSignature(0, 0, 0, 4),
            "denominator" => () => MidiEvent.TimeSignature(0, 0, 4, 3),
            _ => () => MidiEvent.KeySignature(0, 0, 8, false),
        };

        //Act and assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>Every kind describes itself in one line.</summary>
    /// <param name="which">Which kind.</param>
    /// <param name="expected">What the line has to carry.</param>
    [Theory]
    [InlineData("note", "note 60")]
    [InlineData("program", "program 24")]
    [InlineData("control", "controller 7=100")]
    [InlineData("tempo", "bpm")]
    [InlineData("time", "3/4")]
    [InlineData("key", "minor")]
    public void ToString_describes_the_event(string which, string expected)
    {
        //Arrange
        MidiEvent item = which switch
        {
            "note" => MidiEvent.Note(0, 0, 0, 60, 100, 10),
            "program" => MidiEvent.ProgramChange(0, 0, 0, 24),
            "control" => MidiEvent.ControlChange(0, 0, 0, 7, 100),
            "tempo" => MidiEvent.Tempo(0, 0, 90),
            "time" => MidiEvent.TimeSignature(0, 0, 3, 4),
            _ => MidiEvent.KeySignature(0, 0, -2, true),
        };

        //Act and assert
        item.ToString().Should().Contain(expected);
    }
}

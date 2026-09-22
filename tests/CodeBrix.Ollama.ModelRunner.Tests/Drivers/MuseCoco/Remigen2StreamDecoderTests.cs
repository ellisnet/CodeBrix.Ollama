using System;
using System.Collections.Generic;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

public sealed class Remigen2StreamDecoderTests
{
    [Fact]
    public void Add_waits_for_the_bar_boundary_and_preserves_notes_extending_into_later_bars()
    {
        //Arrange
        var decoder = new Remigen2StreamDecoder();

        //Act
        List<MidiEvent> beforeBar = Feed(decoder, "s-9 t-32 o-0 i-40 p-60 d-48 v-20");
        IReadOnlyList<MidiEvent> firstBar = decoder.Add("b-1");

        //Assert
        beforeBar.Should().BeEmpty();
        MidiEvent note = firstBar.Should().ContainSingle(e => e.Kind == MidiEventKind.Note).Which;
        note.NoteNumber.Should().Be(60);
        note.DurationTicks.Should().Be(7200);
        firstBar.Select(e => e.Kind).Should().Equal(MidiEventKind.TimeSignature, MidiEventKind.Tempo,
            MidiEventKind.ProgramChange, MidiEventKind.Note);
    }

    [Fact]
    public void Complete_matches_completed_decoding_at_every_token_cutoff_including_partial_chords()
    {
        //Arrange
        string[] words = ("Q2 s-9 t-32 o-0 i-40 p-60 d-12 v-20 p-64 d-12 v-21 "
            + "o-12 i-0 p-67 d-6 v-20 b-1 s-8 t-24 o-0 i-128 p-164 d-6 v-24 b-1").Split(' ');
        string[] vocabulary = words.Distinct().ToArray();

        //Act and assert
        for (int count = 0; count <= words.Length; count++)
        {
            string[] prefix = words.Take(count).ToArray();
            int[] ids = prefix.Select(word => Array.IndexOf(vocabulary, word)).ToArray();
            MidiScore expected = Remigen2Decoder.Decode(ids, vocabulary);
            var decoder = new Remigen2StreamDecoder();
            var streamed = new List<MidiEvent>();
            foreach (string word in prefix) streamed.AddRange(decoder.Add(word));
            streamed.AddRange(decoder.Complete());
            MusicalNotes(streamed).Should().Equal(MusicalNotes(expected.Events), "cutoff {0} must retain the same notes", count);
            Metadata(streamed).Should().Equal(Metadata(expected.Events), "cutoff {0} must retain the same metadata", count);
            streamed.Select(e => e.Tick).Should().BeInAscendingOrder();
        }
    }

    [Theory]
    [InlineData("p-64")]
    [InlineData("p-64 d-12")]
    public void Complete_does_not_emit_complete_notes_from_a_position_whose_last_note_is_incomplete(string ending)
    {
        //Arrange
        var decoder = new Remigen2StreamDecoder();
        List<MidiEvent> events = Feed(decoder, "s-9 t-32 o-0 i-0 p-55 d-12 v-20 b-1 o-0 p-60 d-12 v-20 " + ending);

        //Act
        events.AddRange(decoder.Complete());

        //Assert
        events.Where(e => e.Kind == MidiEventKind.Note).Should().ContainSingle().Which.NoteNumber.Should().Be(55);
    }

    [Fact]
    public void Add_keeps_channel_assignments_when_lower_programs_and_percussion_arrive_later()
    {
        //Arrange
        var decoder = new Remigen2StreamDecoder();

        //Act
        List<MidiEvent> events = Feed(decoder, "o-0 i-40 p-60 d-12 v-20 b-1 "
            + "o-0 i-128 p-164 d-6 v-20 i-0 p-64 d-12 v-20 i-40 p-67 d-12 v-20 b-1");
        events.AddRange(decoder.Complete());

        //Assert
        MidiEvent[] notes = events.Where(e => e.Kind == MidiEventKind.Note).ToArray();
        notes.Select(n => n.Channel).Should().Equal(0, 9, 1, 0);
        notes.Select(n => n.Track).Should().Equal(1, 2, 3, 1);
        events.Where(e => e.Kind == MidiEventKind.ProgramChange).Select(e => (e.Tick, e.Channel, e.Program))
            .Should().Equal((0L, 0, 40), (1920L, 9, 0), (1920L, 1, 0));
        events.Select(e => e.Tick).Should().BeInAscendingOrder();
        foreach (MidiEvent note in notes)
            events.TakeWhile(e => !ReferenceEquals(e, note)).Should().Contain(e => e.Kind == MidiEventKind.ProgramChange
                && e.Channel == note.Channel && e.Tick <= note.Tick);
    }

    [Fact]
    public void Add_sorts_positions_within_a_bar_and_holds_positions_beyond_its_end()
    {
        //Arrange
        var decoder = new Remigen2StreamDecoder();

        //Act
        List<MidiEvent> first = Feed(decoder, "o-60 p-72 d-12 v-20 o-24 p-64 d-12 v-20 o-0 p-60 d-12 v-20 b-1");
        List<MidiEvent> second = Feed(decoder, "o-0 p-67 d-12 v-20 b-1");
        first.AddRange(second);
        first.AddRange(decoder.Complete());

        //Assert
        first.Where(e => e.Kind == MidiEventKind.Note).Select(e => (e.Tick, e.NoteNumber))
            .Should().Equal((0L, 60), (960L, 64), (1920L, 67), (2400L, 72));
        first.Select(e => e.Tick).Should().BeInAscendingOrder();
        first.Should().OnlyContain(e => e.HorizonTicks == e.Tick);
    }

    [Fact]
    public void Add_applies_signature_changes_to_subsequent_bar_positions_and_keeps_tempo_changes()
    {
        //Arrange
        var decoder = new Remigen2StreamDecoder();

        //Act
        List<MidiEvent> events = Feed(decoder, "s-9 t-32 o-0 p-60 d-12 v-20 b-1 "
            + "s-8 t-24 o-0 p-62 d-12 v-20 b-1 o-0 p-64 d-12 v-20 b-1");
        events.AddRange(decoder.Complete());

        //Assert
        events.Where(e => e.Kind == MidiEventKind.Note).Select(e => e.Tick).Should().Equal(0L, 1920L, 3360L);
        events.Where(e => e.Kind == MidiEventKind.TimeSignature).Select(e => (e.Tick, e.Numerator, e.Denominator))
            .Should().Equal((0L, 4, 4), (1920L, 3, 4));
        events.Where(e => e.Kind == MidiEventKind.Tempo).Select(e => e.Tick).Should().Equal(0L, 1920L);
    }

    [Fact]
    public void Add_establishes_initial_defaults_before_a_later_explicit_tempo_or_signature()
    {
        //Arrange
        var decoder = new Remigen2StreamDecoder();

        //Act
        List<MidiEvent> first = Feed(decoder, "o-0 p-60 d-12 v-20 b-1");
        List<MidiEvent> later = Feed(decoder, "s-8 t-24 o-0 p-64 d-12 v-20 b-1");

        //Assert
        first.Should().Contain(e => e.Kind == MidiEventKind.Tempo && e.Tick == 0 && e.BeatsPerMinute == 120);
        first.Should().Contain(e => e.Kind == MidiEventKind.TimeSignature && e.Tick == 0 && e.Numerator == 4 && e.Denominator == 4);
        later.Should().NotContain(e => e.Tick == 0);
        later.Should().Contain(e => e.Kind == MidiEventKind.Tempo && e.Tick == 1920 && e.BeatsPerMinute == 64);
    }

    [Fact]
    public void Add_supports_fifteen_melodic_programs_plus_percussion_and_refuses_a_sixteenth()
    {
        //Arrange
        var decoder = new Remigen2StreamDecoder();
        var events = new List<MidiEvent>();
        Feed(decoder, "o-0 i-128 p-164 d-6 v-20");
        for (int program = 0; program < 15; program++) Feed(decoder, $"i-{program} p-60 d-12 v-20");

        //Act
        events.AddRange(decoder.Add("b-1"));
        Feed(decoder, "o-0 i-15 p-60 d-12 v-20");
        Action extra = () => decoder.Add("b-1");

        //Assert
        events.Where(e => e.Kind == MidiEventKind.Note).Select(e => e.Channel).Should().OnlyHaveUniqueItems().And.HaveCount(16);
        extra.Should().Throw<InferenceException>().WithMessage("*15 melodic MIDI channels*");
    }

    [Theory]
    [InlineData("d-12 v-20")]
    [InlineData("p-60 v-20")]
    [InlineData("x-1")]
    [InlineData("o--1")]
    [InlineData("s-999")]
    [InlineData("t-49")]
    [InlineData("i-129")]
    [InlineData("p-256 d-12 v-20")]
    [InlineData("p-60 d-12 v-32")]
    public void Add_refuses_invalid_tokens_before_publishing_the_bar(string words)
    {
        //Arrange
        var decoder = new Remigen2StreamDecoder();
        Feed(decoder, words);

        //Act
        Action finishBar = () => decoder.Add("b-1");

        //Assert
        finishBar.Should().Throw<InferenceException>();
    }

    private static List<MidiEvent> Feed(Remigen2StreamDecoder decoder, string text)
    {
        var events = new List<MidiEvent>();
        foreach (string word in text.Split(' ')) events.AddRange(decoder.Add(word));
        return events;
    }

    private static IEnumerable<(long Tick, int Instrument, int Pitch, int Velocity, long Duration)> MusicalNotes(IEnumerable<MidiEvent> source)
    {
        MidiEvent[] events = source.ToArray();
        var programs = events.Where(e => e.Kind == MidiEventKind.ProgramChange).ToDictionary(e => e.Track, e => e.Program);
        return events.Where(e => e.Kind == MidiEventKind.Note)
            .Select(e => (Tick: e.Tick, Instrument: e.Channel == 9 ? 128 : programs[e.Track], Pitch: e.NoteNumber,
                Velocity: e.Velocity, Duration: e.DurationTicks))
            .OrderBy(e => e.Tick).ThenBy(e => e.Instrument).ThenBy(e => e.Pitch).ThenBy(e => e.Duration).ThenBy(e => e.Velocity);
    }

    private static IEnumerable<(MidiEventKind Kind, long Tick, long Tempo, int Numerator, int Denominator)> Metadata(IEnumerable<MidiEvent> source) =>
        source.Where(e => e.Kind is MidiEventKind.Tempo or MidiEventKind.TimeSignature).OrderBy(e => e.Tick).ThenBy(e => e.Kind)
            .Select(e => (e.Kind, e.Tick, e.MicrosecondsPerQuarterNote, e.Numerator, e.Denominator));
}

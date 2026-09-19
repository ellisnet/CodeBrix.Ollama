using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The tokenizer's other direction: a piece of music read into the rows of tokens a model continues from,
/// with the tidying up the model was trained to see.
/// </summary>
/// <remarks>
/// The pieces here are built by hand and are ours. They are small on purpose: each one is shaped to show one
/// thing the tokenizer does, because everything it does happens to every prompt at once and a large piece
/// would prove only that something came out.
/// </remarks>
public sealed class SkyTntTokenizerMidiTests
{
    /// <summary>A piece becomes rows of tokens wrapped in the beginning and ending tokens.</summary>
    [Fact]
    public void Tokenize_wraps_a_piece_in_the_beginning_and_ending_tokens()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[] { MidiEvent.Note(0, 0, 0, 60, 100, 240) });

        //Act
        List<int[]> rows = tokenizer.Tokenize(score, true, 0, 0);

        //Assert
        rows[0][0].Should().Be(tokenizer.BeginningId);
        rows[rows.Count - 1][0].Should().Be(tokenizer.EndId);
    }

    /// <summary>Asked not to, it wraps nothing round the piece.</summary>
    [Fact]
    public void Tokenize_without_the_wrapping_gives_only_the_events()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[] { MidiEvent.Note(0, 0, 0, 60, 100, 240) });

        //Act
        List<int[]> rows = tokenizer.Tokenize(score, false, 0, 0, false, false, false);

        //Assert
        rows.Should().HaveCount(1);
        rows[0][0].Should().Be(tokenizer.TypeForName("note").Id);
    }

    /// <summary>The notes of a piece survive the trip out and back.</summary>
    [Fact]
    public void Tokenize_and_Detokenize_keep_the_notes()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 240),
            MidiEvent.Note(480, 0, 0, 67, 90, 480),
            MidiEvent.Note(1440, 0, 0, 60, 80, 120),
        });

        //Act
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));

        //Assert
        List<MidiEvent> notes = Notes(back);
        notes.Should().HaveCount(3);
        Describe(notes[0]).Should().Be("0 60 100 240");
        Describe(notes[1]).Should().Be("480 67 90 480");
        Describe(notes[2]).Should().Be("1440 60 80 120");
    }

    /// <summary>A position off the sixteenth it belongs to is rounded to the nearest one.</summary>
    /// <param name="tick">Where the note is.</param>
    /// <param name="expected">Where it lands.</param>
    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(14L, 0L)]
    [InlineData(16L, 30L)]
    [InlineData(30L, 30L)]
    [InlineData(44L, 30L)]
    [InlineData(46L, 60L)]
    [InlineData(480L, 480L)]
    public void Tokenize_rounds_a_position_to_the_nearest_sixteenth(long tick, long expected)
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[] { MidiEvent.Note(tick, 0, 0, 60, 100, 240) });

        //Act
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));

        //Assert
        Notes(back)[0].Tick.Should().Be(expected);
    }

    /// <summary>A note too short to round to anything is given the shortest length there is.</summary>
    [Fact]
    public void Tokenize_gives_a_note_too_short_to_round_the_shortest_length_there_is()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[] { MidiEvent.Note(0, 0, 0, 60, 100, 3) });

        //Act
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));

        //Assert
        Notes(back)[0].DurationTicks.Should().Be(30);
    }

    /// <summary>A piece at another resolution is read at the model's own.</summary>
    [Fact]
    public void Tokenize_reads_a_piece_at_another_resolution()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(96, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 48),
            MidiEvent.Note(96, 0, 0, 62, 100, 96),
        });

        //Act
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));

        //Assert
        List<MidiEvent> notes = Notes(back);
        back.TicksPerQuarterNote.Should().Be(480);
        Describe(notes[0]).Should().Be("0 60 100 240");
        Describe(notes[1]).Should().Be("480 62 100 480");
    }

    /// <summary>Channels are re-numbered into the order they are first used.</summary>
    [Fact]
    public void Tokenize_renumbers_channels_into_the_order_they_are_used()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 5, 60, 100, 240),
            MidiEvent.Note(480, 0, 7, 67, 100, 240),
        });

        //Act
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));

        //Assert
        List<MidiEvent> notes = Notes(back);
        notes[0].Channel.Should().Be(0);
        notes[1].Channel.Should().Be(1);
    }

    /// <summary>The percussion channel stays where it is, wherever else the others move.</summary>
    [Fact]
    public void Tokenize_leaves_the_percussion_channel_where_it_is()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 9, 36, 100, 120),
            MidiEvent.Note(0, 0, 4, 60, 100, 240),
        });

        //Act
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));

        //Assert
        List<int> channels = new List<int>();
        foreach (MidiEvent note in Notes(back)) channels.Add(note.Channel);
        channels.Should().Contain(9);
        channels.Should().Contain(0);
    }

    /// <summary>A channel that never chose an instrument is given one.</summary>
    [Fact]
    public void Tokenize_gives_an_instrument_to_a_channel_that_never_chose_one()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[] { MidiEvent.Note(0, 0, 0, 60, 100, 240) });

        //Act
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));

        //Assert
        MidiEvent instrument = Find(back, MidiEventKind.ProgramChange);
        instrument.Should().NotBeNull();
        instrument.Program.Should().Be(0);
        instrument.Channel.Should().Be(0);
    }

    /// <summary>Asked not to tidy, it gives no instrument to a channel that chose none.</summary>
    [Fact]
    public void Tokenize_without_the_tidying_adds_no_instrument()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[] { MidiEvent.Note(0, 0, 0, 60, 100, 240) });

        //Act
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(score, false, 0, 0, false, false, false));

        //Assert
        Find(back, MidiEventKind.ProgramChange).Should().BeNull();
    }

    /// <summary>A controller change that barely moves the controller is left out.</summary>
    [Fact]
    public void Tokenize_leaves_out_a_controller_change_that_barely_moves()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 240),
            MidiEvent.ControlChange(480, 0, 0, 7, 100),
            MidiEvent.ControlChange(960, 0, 0, 7, 102),
            MidiEvent.ControlChange(1440, 0, 0, 7, 40),
        });

        //Act
        MidiScore kept = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 4, 4));
        MidiScore all = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));

        //Assert
        Count(kept, MidiEventKind.ControlChange).Should().Be(2);
        Count(all, MidiEventKind.ControlChange).Should().Be(3);
    }

    /// <summary>A change of speed that barely changes it is left out.</summary>
    [Fact]
    public void Tokenize_leaves_out_a_change_of_speed_that_barely_changes_it()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 240),
            MidiEvent.Tempo(0, 0, 120),
            MidiEvent.Tempo(480, 0, 121),
            MidiEvent.Tempo(960, 0, 90),
        });

        //Act
        MidiScore kept = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 4, 4));

        //Assert
        Count(kept, MidiEventKind.Tempo).Should().Be(2);
    }

    /// <summary>A channel that carries no notes at all is dropped.</summary>
    [Fact]
    public void Tokenize_drops_a_channel_that_carries_no_notes()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 240),
            MidiEvent.ControlChange(0, 0, 3, 7, 100),
        });

        //Act
        MidiScore tidied = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));
        MidiScore untidied = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0, false, false, false));

        //Assert
        Count(tidied, MidiEventKind.ControlChange).Should().Be(0);
        Count(untidied, MidiEventKind.ControlChange).Should().Be(1);
    }

    /// <summary>Everything before the first note is gathered into a setup at the front of the piece.</summary>
    [Fact]
    public void Tokenize_gathers_everything_before_the_first_note_into_a_setup()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Tempo(0, 0, 100),
            MidiEvent.ProgramChange(960, 0, 0, 24),
            MidiEvent.ControlChange(1440, 0, 0, 7, 100),
            MidiEvent.Note(1920, 0, 0, 60, 100, 240),
        });

        //Act
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));

        //Assert
        Find(back, MidiEventKind.ProgramChange).Tick.Should().Be(0);
        Find(back, MidiEventKind.ControlChange).Tick.Should().Be(0);
        Find(back, MidiEventKind.Tempo).Tick.Should().Be(0);
        Find(back, MidiEventKind.Note).Tick.Should().Be(1920);
    }

    /// <summary>Two notes of one pitch that overlap come out one after the other, never on top of each other.</summary>
    /// <remarks>
    /// A FILE CANNOT SAY WHICH ENDING BELONGS TO WHICH BEGINNING when the same pitch sounds twice on one
    /// channel at once - both halves are the same three bytes - so a reader pairs them in the order they
    /// arrive, and the piece that comes back out of a file is two notes that touch rather than one inside the
    /// other. That is what the publisher's own reader does with the same file, and this is the case that
    /// shows it.
    /// </remarks>
    [Fact]
    public void Tokenize_of_two_overlapping_notes_of_one_pitch_gives_two_that_touch()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore written = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 960),
            MidiEvent.Note(480, 0, 0, 60, 100, 240),
        });

        //Act
        MidiScore read = MidiFile.Read(MidiFile.Write(written));
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(read, true, 0, 0));

        //Assert
        List<MidiEvent> notes = Notes(back);
        notes.Should().HaveCount(2);
        Describe(notes[0]).Should().Be("0 60 100 480");
        Describe(notes[1]).Should().Be("480 60 100 480");
    }

    /// <summary>
    /// A note another note of the same pitch encloses entirely is dropped, because the rounding leaves
    /// nothing of it.
    /// </summary>
    /// <remarks>
    /// It cannot come out of a file - a file pairs the endings in order - but it can come out of a piece built
    /// in memory, and what the tokenizer does with it is the publisher's own arithmetic: the earlier note is
    /// cut back to where the later one begins, and a note cut back to nothing is taken out.
    /// </remarks>
    [Fact]
    public void Tokenize_drops_a_note_another_of_its_pitch_encloses()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 100, 960),
            MidiEvent.Note(480, 0, 0, 60, 100, 240),
        });

        //Act
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(score, true, 0, 0));

        //Assert
        List<MidiEvent> notes = Notes(back);
        notes.Should().HaveCount(1);
        Describe(notes[0]).Should().Be("0 60 100 960");
    }

    /// <summary>A piece the file writer wrote is read back through the tokenizer with its notes intact.</summary>
    [Fact]
    public void Tokenize_of_a_piece_read_from_a_file_keeps_the_notes()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore written = new MidiScore(480, new[]
        {
            MidiEvent.Tempo(0, 0, 96),
            MidiEvent.ProgramChange(0, 1, 0, 40),
            MidiEvent.Note(0, 1, 0, 64, 100, 480),
            MidiEvent.Note(480, 1, 0, 67, 100, 480),
            MidiEvent.Note(960, 2, 9, 36, 110, 120),
        });

        //Act
        MidiScore read = MidiFile.Read(MidiFile.Write(written));
        MidiScore back = tokenizer.Detokenize(tokenizer.Tokenize(read, true, 0, 0));

        //Assert
        List<MidiEvent> notes = Notes(back);
        notes.Should().HaveCount(3);
        Describe(notes[0]).Should().Be("0 64 100 480");
        Describe(notes[1]).Should().Be("480 67 100 480");
        Describe(notes[2]).Should().Be("960 36 110 120");
    }

    /// <summary>A piece with nothing in it becomes nothing but the wrapping.</summary>
    [Fact]
    public void Tokenize_of_an_empty_piece_is_only_the_wrapping()
    {
        //Arrange
        SkyTntTokenizer tokenizer = SkyTntFixtures.Tokenizer();
        MidiScore score = new MidiScore(480, System.Array.Empty<MidiEvent>());

        //Act
        List<int[]> rows = tokenizer.Tokenize(score, true, 0, 0);

        //Assert
        rows.Should().HaveCount(2);
        rows[0][0].Should().Be(tokenizer.BeginningId);
        rows[1][0].Should().Be(tokenizer.EndId);
    }

    private static List<MidiEvent> Notes(MidiScore score)
    {
        List<MidiEvent> notes = new List<MidiEvent>();
        foreach (MidiEvent item in score.Events)
        {
            if (item.Kind == MidiEventKind.Note) notes.Add(item);
        }

        return notes;
    }

    private static MidiEvent Find(MidiScore score, MidiEventKind kind)
    {
        foreach (MidiEvent item in score.Events)
        {
            if (item.Kind == kind) return item;
        }

        return null;
    }

    private static int Count(MidiScore score, MidiEventKind kind)
    {
        int count = 0;
        foreach (MidiEvent item in score.Events)
        {
            if (item.Kind == kind) count++;
        }

        return count;
    }

    private static string Describe(MidiEvent note) =>
        note.Tick + " " + note.NoteNumber + " " + note.Velocity + " " + note.DurationTicks;
}

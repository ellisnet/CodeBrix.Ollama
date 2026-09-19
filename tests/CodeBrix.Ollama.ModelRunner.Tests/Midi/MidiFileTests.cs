using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The Standard MIDI File writer and reader, against the published format.
/// </summary>
/// <remarks>
/// THE FIRST TEST SPELLS OUT EVERY BYTE. A round trip through code of one's own proves only that the two
/// halves agree with each other, so the shape of the file is pinned against the format itself: the header
/// chunk, the track chunk, the delta times, the running-status shorthand, and the three meta events.
/// </remarks>
public sealed class MidiFileTests
{
    /// <summary>A small piece is written as exactly the bytes the format defines.</summary>
    [Fact]
    public void Write_produces_the_bytes_the_format_defines()
    {
        //Arrange
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Tempo(0, 0, 120),
            MidiEvent.ProgramChange(0, 0, 0, 42),
            MidiEvent.Note(0, 0, 0, 60, 100, 240),
            MidiEvent.Note(240, 0, 0, 62, 100, 240),
        });

        //Act
        byte[] bytes = MidiFile.Write(score);

        //Assert
        bytes.Should().Equal(new byte[]
        {
            // MThd, six bytes of payload, format 0 (one track), one track, 480 ticks a quarter note.
            0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01, 0x01, 0xE0,

            // MTrk, thirty-two bytes.
            0x4D, 0x54, 0x72, 0x6B, 0x00, 0x00, 0x00, 0x20,

            0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20,  // at once: half a million microseconds a beat
            0x00, 0xC0, 0x2A,                          // at once: instrument 42 on channel 0
            0x00, 0x90, 0x3C, 0x64,                    // at once: middle C begins
            0x81, 0x70, 0x80, 0x3C, 0x00,              // 240 ticks later: middle C ends
            0x00, 0x90, 0x3E, 0x64,                    // at once: the D begins
            0x81, 0x70, 0x80, 0x3E, 0x00,              // 240 ticks later: the D ends
            0x00, 0xFF, 0x2F, 0x00,                    // and the track ends
        });
    }

    /// <summary>A repeated channel message leaves its status byte out.</summary>
    [Fact]
    public void Write_uses_running_status_for_a_repeated_channel_message()
    {
        //Arrange
        MidiScore score = new MidiScore(96, new[]
        {
            MidiEvent.ControlChange(0, 0, 0, 7, 100),
            MidiEvent.ControlChange(0, 0, 0, 10, 64),
        });

        //Act
        byte[] bytes = MidiFile.Write(score);

        //Assert
        Track(bytes).Should().Equal(new byte[]
        {
            0x00, 0xB0, 0x07, 0x64,
            0x00, 0x0A, 0x40,        // the second one leaves 0xB0 out
            0x00, 0xFF, 0x2F, 0x00,
        });
    }

    /// <summary>Asked not to, it spells every status byte out.</summary>
    [Fact]
    public void Write_without_running_status_spells_every_status_byte_out()
    {
        //Arrange
        MidiScore score = new MidiScore(96, new[]
        {
            MidiEvent.ControlChange(0, 0, 0, 7, 100),
            MidiEvent.ControlChange(0, 0, 0, 10, 64),
        });

        //Act
        byte[] bytes = MidiFile.Write(score, runningStatus: false);

        //Assert
        Track(bytes).Should().Equal(new byte[]
        {
            0x00, 0xB0, 0x07, 0x64,
            0x00, 0xB0, 0x0A, 0x40,
            0x00, 0xFF, 0x2F, 0x00,
        });
    }

    /// <summary>A meta event ends a run, so the next channel message states its status again.</summary>
    [Fact]
    public void Write_ends_a_run_of_channel_messages_at_a_meta_event()
    {
        //Arrange
        MidiScore score = new MidiScore(96, new[]
        {
            MidiEvent.ControlChange(0, 0, 0, 7, 100),
            MidiEvent.Tempo(10, 0, 60),
            MidiEvent.ControlChange(20, 0, 0, 10, 64),
        });

        //Act
        byte[] bytes = MidiFile.Write(score);

        //Assert
        Track(bytes).Should().Equal(new byte[]
        {
            0x00, 0xB0, 0x07, 0x64,
            0x0A, 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40,
            0x0A, 0xB0, 0x0A, 0x40,
            0x00, 0xFF, 0x2F, 0x00,
        });
    }

    /// <summary>A piece of several tracks is written as format one, one chunk each.</summary>
    [Fact]
    public void Write_of_several_tracks_is_format_one()
    {
        //Arrange
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Tempo(0, 0, 90),
            MidiEvent.Note(0, 1, 0, 60, 90, 120),
            MidiEvent.Note(0, 2, 1, 67, 90, 120),
        });

        //Act
        byte[] bytes = MidiFile.Write(score);

        //Assert
        bytes[9].Should().Be(1);
        bytes[11].Should().Be(3);
        TrackChunks(bytes).Should().Be(3);
    }

    /// <summary>A time signature and a key signature are written as the format defines them.</summary>
    [Fact]
    public void Write_of_the_signatures_is_the_format_defines()
    {
        //Arrange
        MidiScore score = new MidiScore(96, new[]
        {
            MidiEvent.TimeSignature(0, 0, 6, 8),
            MidiEvent.KeySignature(0, 0, -3, true),
        });

        //Act
        byte[] bytes = MidiFile.Write(score);

        //Assert
        Track(bytes).Should().Equal(new byte[]
        {
            0x00, 0xFF, 0x58, 0x04, 0x06, 0x03, 0x18, 0x08,  // 6/8: the lower number as the power it is
            0x00, 0xFF, 0x59, 0x02, 0xFD, 0x01,              // three flats, minor
            0x00, 0xFF, 0x2F, 0x00,
        });
    }

    /// <summary>A piece with nothing in it is still a file with one track in it.</summary>
    [Fact]
    public void Write_of_an_empty_piece_is_one_empty_track()
    {
        //Arrange
        MidiScore score = new MidiScore(120, Array.Empty<MidiEvent>());

        //Act
        byte[] bytes = MidiFile.Write(score);

        //Assert
        bytes.Should().Equal(new byte[]
        {
            0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01, 0x00, 0x78,
            0x4D, 0x54, 0x72, 0x6B, 0x00, 0x00, 0x00, 0x04, 0x00, 0xFF, 0x2F, 0x00,
        });
    }

    /// <summary>What is written comes back as it was.</summary>
    [Fact]
    public void Read_of_what_Write_produced_gives_the_same_piece()
    {
        //Arrange
        MidiScore score = new MidiScore(384, new[]
        {
            MidiEvent.TimeSignature(0, 0, 3, 4),
            MidiEvent.KeySignature(0, 0, 2, false),
            MidiEvent.Tempo(0, 0, 138),
            MidiEvent.ProgramChange(0, 1, 0, 24),
            MidiEvent.ControlChange(0, 1, 0, 7, 110),
            MidiEvent.Note(0, 1, 0, 64, 88, 192),
            MidiEvent.Note(192, 1, 0, 67, 88, 192),
            MidiEvent.Note(384, 2, 9, 36, 127, 96),
        });

        //Act
        MidiScore read = MidiFile.Read(MidiFile.Write(score));

        //Assert
        read.TicksPerQuarterNote.Should().Be(384);
        read.Events.Should().HaveCount(score.Events.Count);
        for (int i = 0; i < read.Events.Count; i++)
        {
            Describe(read.Events[i]).Should().Be(Describe(score.Events[i]));
        }
    }

    /// <summary>A tempo comes back to the microsecond, which is what the file actually holds.</summary>
    [Fact]
    public void Read_keeps_a_tempo_to_the_microsecond()
    {
        //Arrange
        MidiScore score = new MidiScore(480, new[] { MidiEvent.TempoFromMicroseconds(0, 0, 543211) });

        //Act
        MidiScore read = MidiFile.Read(MidiFile.Write(score));

        //Assert
        read.Events[0].MicrosecondsPerQuarterNote.Should().Be(543211);
    }

    /// <summary>A note begun with no loudness at all is an ending, as the format allows.</summary>
    [Fact]
    public void Read_treats_a_beginning_with_no_loudness_as_an_ending()
    {
        //Arrange
        byte[] bytes = File(96, new byte[]
        {
            0x00, 0x90, 0x3C, 0x40,  // middle C begins
            0x60, 0x90, 0x3C, 0x00,  // 96 ticks later, it begins with no loudness, which ends it
            0x00, 0xFF, 0x2F, 0x00,
        });

        //Act
        MidiScore read = MidiFile.Read(bytes);

        //Assert
        read.Events.Should().HaveCount(1);
        read.Events[0].Kind.Should().Be(MidiEventKind.Note);
        read.Events[0].DurationTicks.Should().Be(96);
    }

    /// <summary>A file written with the shorthand is read back the same as one written without it.</summary>
    [Fact]
    public void Read_follows_running_status()
    {
        //Arrange
        byte[] bytes = File(96, new byte[]
        {
            0x00, 0x90, 0x3C, 0x40,
            0x00, 0x3E, 0x40,        // a second beginning with no status byte of its own
            0x60, 0x80, 0x3C, 0x00,
            0x00, 0x3E, 0x00,        // and a second ending the same way
            0x00, 0xFF, 0x2F, 0x00,
        });

        //Act
        MidiScore read = MidiFile.Read(bytes);

        //Assert
        read.Events.Should().HaveCount(2);
        read.Events[0].NoteNumber.Should().Be(60);
        read.Events[1].NoteNumber.Should().Be(62);
        read.Events[0].DurationTicks.Should().Be(96);
        read.Events[1].DurationTicks.Should().Be(96);
    }

    /// <summary>Everything this library has no event for is stepped over by the length it states.</summary>
    [Fact]
    public void Read_steps_over_what_it_has_no_event_for()
    {
        //Arrange
        byte[] bytes = File(96, new byte[]
        {
            0x00, 0xFF, 0x03, 0x04, 0x4E, 0x61, 0x6D, 0x65,  // a track name
            0x00, 0xF0, 0x03, 0x7D, 0x01, 0xF7,              // a system-exclusive message
            0x00, 0xE0, 0x00, 0x40,                          // a pitch bend
            0x00, 0xA0, 0x3C, 0x40,                          // aftertouch on one note
            0x00, 0xD0, 0x40,                                // aftertouch on the channel
            0x00, 0x90, 0x3C, 0x40,
            0x60, 0x80, 0x3C, 0x00,
            0x00, 0xFF, 0x2F, 0x00,
        });

        //Act
        MidiScore read = MidiFile.Read(bytes);

        //Assert
        read.Events.Should().HaveCount(1);
        read.Events[0].Kind.Should().Be(MidiEventKind.Note);
    }

    /// <summary>A note the file never ended stops where its track does.</summary>
    [Fact]
    public void Read_ends_a_note_the_file_left_sounding()
    {
        //Arrange
        byte[] bytes = File(96, new byte[]
        {
            0x00, 0x90, 0x3C, 0x40,
            0x60, 0xB0, 0x07, 0x64,
            0x00, 0xFF, 0x2F, 0x00,
        });

        //Act
        MidiScore read = MidiFile.Read(bytes);

        //Assert
        read.Events.Should().HaveCount(2);
        read.Events[0].Kind.Should().Be(MidiEventKind.Note);
        read.Events[0].DurationTicks.Should().Be(96);
    }

    /// <summary>A file that is not one is refused, and the message says what it starts with instead.</summary>
    [Fact]
    public void Read_of_something_that_is_not_a_midi_file_is_refused()
    {
        //Arrange
        Action act = () => MidiFile.Read(new byte[]
        {
            0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        });

        //Act and assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("RIFF");
    }

    /// <summary>A file that measures time in frames of film is refused rather than misread.</summary>
    [Fact]
    public void Read_of_a_file_measured_in_frames_is_refused()
    {
        //Arrange
        byte[] bytes = File(96, new byte[] { 0x00, 0xFF, 0x2F, 0x00 });
        bytes[12] = 0xE7;
        bytes[13] = 0x28;
        Action act = () => MidiFile.Read(bytes);

        //Act and assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("frames of film");
    }

    /// <summary>A file cut short is refused rather than read as far as it goes.</summary>
    [Fact]
    public void Read_of_a_file_cut_short_is_refused()
    {
        //Arrange
        byte[] whole = File(96, new byte[] { 0x00, 0x90, 0x3C, 0x40, 0x60, 0x80, 0x3C, 0x00, 0x00, 0xFF, 0x2F, 0x00 });
        byte[] cut = new byte[whole.Length - 4];
        Array.Copy(whole, cut, cut.Length);
        Action act = () => MidiFile.Read(cut);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A file whose first event has no status byte to take is refused.</summary>
    [Fact]
    public void Read_of_a_track_starting_without_a_status_byte_is_refused()
    {
        //Arrange
        Action act = () => MidiFile.Read(File(96, new byte[] { 0x00, 0x3C, 0x40, 0x00, 0xFF, 0x2F, 0x00 }));

        //Act and assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("status");
    }

    /// <summary>Writing needs a piece.</summary>
    [Fact]
    public void Write_of_nothing_is_refused()
    {
        //Arrange
        Action act = () => MidiFile.Write(null);

        //Act and assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Reading needs bytes.</summary>
    [Fact]
    public void Read_of_nothing_is_refused()
    {
        //Arrange
        Action act = () => MidiFile.Read(null);

        //Act and assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>A file is written to disk and read back.</summary>
    /// <returns>A task that completes when the file has been written and read.</returns>
    [Fact]
    public async System.Threading.Tasks.Task WriteAsync_and_ReadAsync_go_through_a_file()
    {
        //Arrange
        MidiScore score = new MidiScore(240, new[] { MidiEvent.Note(0, 0, 3, 55, 70, 120) });
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "codebrix-ollama-midi-" + Guid.NewGuid().ToString("N") + ".mid");

        try
        {
            //Act
            await MidiFile.WriteAsync(path, score, TestContext.Current.CancellationToken);
            MidiScore read = await MidiFile.ReadAsync(path, TestContext.Current.CancellationToken);

            //Assert
            read.Events.Should().HaveCount(1);
            read.Events[0].NoteNumber.Should().Be(55);
            read.Events[0].Channel.Should().Be(3);
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    private static byte[] File(int division, byte[] track)
    {
        List<byte> bytes = new List<byte>
        {
            0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00, 0x01,
            (byte)(division >> 8), (byte)(division & 0xFF),
            0x4D, 0x54, 0x72, 0x6B,
            (byte)((track.Length >> 24) & 0xFF), (byte)((track.Length >> 16) & 0xFF),
            (byte)((track.Length >> 8) & 0xFF), (byte)(track.Length & 0xFF),
        };

        bytes.AddRange(track);
        return bytes.ToArray();
    }

    private static byte[] Track(byte[] file)
    {
        byte[] track = new byte[file.Length - 22];
        Array.Copy(file, 22, track, 0, track.Length);
        return track;
    }

    private static int TrackChunks(byte[] file)
    {
        int chunks = 0;
        int at = 14;
        while (at + 8 <= file.Length)
        {
            long length = ((long)file[at + 4] << 24) | ((long)file[at + 5] << 16)
                | ((long)file[at + 6] << 8) | file[at + 7];
            at += 8 + (int)length;
            chunks++;
        }

        return chunks;
    }

    private static string Describe(MidiEvent item) =>
        item.Kind + " " + item.Tick + " " + item.Track + " " + item.Channel + " " + item.NoteNumber + " "
        + item.Velocity + " " + item.DurationTicks + " " + item.Program + " " + item.Controller + " "
        + item.Value + " " + item.MicrosecondsPerQuarterNote + " " + item.Numerator + " " + item.Denominator
        + " " + item.SharpsOrFlats + " " + item.IsMinor;
}

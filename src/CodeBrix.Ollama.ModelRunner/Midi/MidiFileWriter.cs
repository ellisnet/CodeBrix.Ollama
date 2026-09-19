using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Turns a <see cref="MidiScore"/> into the bytes of a Standard MIDI File.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS OURS, WRITTEN FROM THE PUBLISHED FORMAT and not ported from anything: the header chunk
/// (<c>MThd</c>, six bytes of payload, a format word, a track count and a division), one track chunk
/// (<c>MTrk</c>) per track, a variable-length delta time before every event, the four channel messages this
/// library produces, the three meta events it produces, and the end-of-track meta event every track must
/// finish with.
/// </para>
/// <para>
/// A SCORE HOLDS NOTES WITH LENGTHS AND A FILE HOLDS BEGINNINGS AND ENDINGS, so each note becomes two
/// messages. Within one position the endings are written first, then anything that sets an instrument, a
/// controller, a tempo or a signature, then the beginnings - which is what lets a note trimmed to end exactly
/// where the next one of the same pitch begins be heard as two notes rather than one silence.
/// </para>
/// <para>
/// RUNNING STATUS is the format's own shorthand: a channel message may leave its status byte out when it is
/// the same as the one before. It is used here by default because it makes the file smaller and every reader
/// understands it; a meta event ends the run, as the format requires.
/// </para>
/// </remarks>
internal static class MidiFileWriter
{
    /// <summary>The largest delta a variable-length quantity can hold: four bytes of seven bits each.</summary>
    internal const long MaximumVariableLengthQuantity = 0x0FFFFFFF;

    /// <summary>Writes a score out as the bytes of a Standard MIDI File.</summary>
    /// <param name="score">The piece to write.</param>
    /// <param name="runningStatus">Whether to leave out a channel message's status byte when it repeats.</param>
    /// <returns>The file's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="score"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The piece is longer than a file's delta times can express, or uses more tracks than a file can hold.
    /// </exception>
    internal static byte[] Write(MidiScore score, bool runningStatus)
    {
        if (score == null) throw new ArgumentNullException(nameof(score));

        List<int> tracks = TrackNumbers(score);
        if (tracks.Count > ushort.MaxValue)
        {
            throw new ArgumentException(
                "A Standard MIDI File holds at most " + ushort.MaxValue.ToString(CultureInfo.InvariantCulture)
                + " tracks and this piece uses " + tracks.Count.ToString(CultureInfo.InvariantCulture) + ".",
                nameof(score));
        }

        using MemoryStream file = new MemoryStream();

        //The header chunk: six bytes of payload, then the format, the number of tracks and the division. A
        //piece of one track is format 0 by convention, and anything else is format 1 - one track per part,
        //all playing at once.
        Write(file, (byte)'M', (byte)'T', (byte)'h', (byte)'d');
        WriteBigEndian32(file, 6);
        WriteBigEndian16(file, tracks.Count <= 1 ? 0 : 1);
        WriteBigEndian16(file, tracks.Count == 0 ? 1 : tracks.Count);
        WriteBigEndian16(file, score.TicksPerQuarterNote);

        if (tracks.Count == 0)
        {
            //A file must hold at least one track, and an empty piece is one empty track.
            WriteTrack(file, Array.Empty<MidiFileMessage>(), runningStatus);
            return file.ToArray();
        }

        foreach (int track in tracks)
        {
            WriteTrack(file, Messages(score, track), runningStatus);
        }

        return file.ToArray();
    }

    /// <summary>Encodes a number as a variable-length quantity: seven bits a byte, high bit set but on the last.</summary>
    /// <param name="stream">Where to write it.</param>
    /// <param name="value">The number, 0 to <see cref="MaximumVariableLengthQuantity"/>.</param>
    /// <exception cref="ArgumentException">The number is negative or too large for four bytes.</exception>
    internal static void WriteVariableLengthQuantity(Stream stream, long value)
    {
        if (value < 0 || value > MaximumVariableLengthQuantity)
        {
            throw new ArgumentException(
                "A Standard MIDI File holds a delta time in at most four seven-bit bytes, so it is 0 to "
                + MaximumVariableLengthQuantity.ToString(CultureInfo.InvariantCulture) + "; this one is "
                + value.ToString(CultureInfo.InvariantCulture) + ".",
                nameof(value));
        }

        //The low seven bits go last, so the bytes are gathered from the bottom up and written from the top
        //down. Nought is one byte of nought, not nothing at all.
        Span<byte> bytes = stackalloc byte[4];
        int count = 0;
        long remaining = value;
        do
        {
            bytes[count++] = (byte)(remaining & 0x7F);
            remaining >>= 7;
        }
        while (remaining != 0);

        for (int i = count - 1; i >= 0; i--)
        {
            stream.WriteByte(i == 0 ? bytes[i] : (byte)(bytes[i] | 0x80));
        }
    }

    private static List<int> TrackNumbers(MidiScore score)
    {
        SortedSet<int> used = new SortedSet<int>();
        foreach (MidiEvent item in score.Events) used.Add(item.Track);
        return new List<int>(used);
    }

    private static List<MidiFileMessage> Messages(MidiScore score, int track)
    {
        List<MidiFileMessage> messages = new List<MidiFileMessage>();
        int sequence = 0;

        foreach (MidiEvent item in score.Events)
        {
            if (item.Track != track) continue;

            switch (item.Kind)
            {
                case MidiEventKind.Note:
                    messages.Add(new MidiFileMessage(
                        item.Tick, MidiFileMessagePhase.NoteOn, sequence++,
                        new byte[]
                        {
                            (byte)(0x90 | item.Channel), (byte)item.NoteNumber, (byte)item.Velocity,
                        }));
                    messages.Add(new MidiFileMessage(
                        item.Tick + item.DurationTicks, MidiFileMessagePhase.NoteOff, sequence++,
                        new byte[] { (byte)(0x80 | item.Channel), (byte)item.NoteNumber, 0 }));
                    break;

                case MidiEventKind.ProgramChange:
                    messages.Add(new MidiFileMessage(
                        item.Tick, MidiFileMessagePhase.Setup, sequence++,
                        new byte[] { (byte)(0xC0 | item.Channel), (byte)item.Program }));
                    break;

                case MidiEventKind.ControlChange:
                    messages.Add(new MidiFileMessage(
                        item.Tick, MidiFileMessagePhase.Setup, sequence++,
                        new byte[] { (byte)(0xB0 | item.Channel), (byte)item.Controller, (byte)item.Value }));
                    break;

                case MidiEventKind.Tempo:
                    messages.Add(new MidiFileMessage(
                        item.Tick, MidiFileMessagePhase.Setup, sequence++,
                        new byte[]
                        {
                            0xFF, 0x51, 0x03,
                            (byte)((item.MicrosecondsPerQuarterNote >> 16) & 0xFF),
                            (byte)((item.MicrosecondsPerQuarterNote >> 8) & 0xFF),
                            (byte)(item.MicrosecondsPerQuarterNote & 0xFF),
                        }));
                    break;

                case MidiEventKind.TimeSignature:
                    //The lower number is stored as the power of two it is, and the last two bytes are the
                    //metronome's click in clocks and how many thirty-second notes go to a quarter note. The
                    //usual 24 and 8 are what every writer puts there.
                    messages.Add(new MidiFileMessage(
                        item.Tick, MidiFileMessagePhase.Setup, sequence++,
                        new byte[]
                        {
                            0xFF, 0x58, 0x04, (byte)item.Numerator, Power(item.Denominator), 24, 8,
                        }));
                    break;

                case MidiEventKind.KeySignature:
                    messages.Add(new MidiFileMessage(
                        item.Tick, MidiFileMessagePhase.Setup, sequence++,
                        new byte[]
                        {
                            0xFF, 0x59, 0x02, (byte)(sbyte)item.SharpsOrFlats, (byte)(item.IsMinor ? 1 : 0),
                        }));
                    break;
            }
        }

        messages.Sort(MidiFileMessage.Compare);
        return messages;
    }

    private static void WriteTrack(Stream file, IReadOnlyList<MidiFileMessage> messages, bool runningStatus)
    {
        using MemoryStream track = new MemoryStream();

        long at = 0;
        int status = -1;
        foreach (MidiFileMessage message in messages)
        {
            WriteVariableLengthQuantity(track, message.Tick - at);
            at = message.Tick;

            byte first = message.Bytes[0];
            if (first >= 0xF0)
            {
                //A meta event (and a system-exclusive one) ends a run of channel messages, so the next
                //channel message has to state its status again.
                status = -1;
                track.Write(message.Bytes, 0, message.Bytes.Length);
                continue;
            }

            if (runningStatus && first == status)
            {
                track.Write(message.Bytes, 1, message.Bytes.Length - 1);
                continue;
            }

            status = first;
            track.Write(message.Bytes, 0, message.Bytes.Length);
        }

        //Every track ends with the end-of-track meta event, at no distance from the last thing in it.
        WriteVariableLengthQuantity(track, 0);
        track.WriteByte(0xFF);
        track.WriteByte(0x2F);
        track.WriteByte(0x00);

        byte[] bytes = track.ToArray();
        Write(file, (byte)'M', (byte)'T', (byte)'r', (byte)'k');
        WriteBigEndian32(file, bytes.Length);
        file.Write(bytes, 0, bytes.Length);
    }

    private static byte Power(int denominator)
    {
        byte power = 0;
        int value = denominator;
        while (value > 1)
        {
            value >>= 1;
            power++;
        }

        return power;
    }

    private static void Write(Stream stream, byte a, byte b, byte c, byte d)
    {
        stream.WriteByte(a);
        stream.WriteByte(b);
        stream.WriteByte(c);
        stream.WriteByte(d);
    }

    private static void WriteBigEndian32(Stream stream, long value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteBigEndian16(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }
}

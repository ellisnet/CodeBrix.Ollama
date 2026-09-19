using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Reads the bytes of a Standard MIDI File into a <see cref="MidiScore"/>.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS OURS, WRITTEN FROM THE PUBLISHED FORMAT and not ported from anything. It reads the header chunk,
/// every track chunk, variable-length delta times, running status, the four channel messages this library
/// understands and the three meta events it understands - and STEPS OVER everything else correctly, which is
/// the part a reader has to get right: an unknown meta event states its own length, a system-exclusive message
/// states its own length, and the channel messages it does not use have a known number of bytes each.
/// </para>
/// <para>
/// NOTES ARE PAIRED UP. A file says a note begins and later that it ends; a score says a note begins and how
/// long it lasts. A beginning with a loudness of nought is an ending, as the format allows. Several beginnings
/// of one pitch on one channel before any ending are matched to endings in the order they arrived, and a note
/// still sounding when its track runs out is ended there.
/// </para>
/// <para>
/// WHAT IS DROPPED, and it is said here rather than left to be discovered: a note whose beginning and ending
/// are at the same tick (it can never be heard), and every message this library has no event for - pitch bend,
/// aftertouch, system-exclusive data, and the text meta events. Positions are read exactly as the file states
/// them, so the file's own ticks per quarter note come through unchanged.
/// </para>
/// </remarks>
internal static class MidiFileReader
{
    /// <summary>Reads a file's bytes into a score.</summary>
    /// <param name="bytes">The whole file.</param>
    /// <returns>The piece the file holds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// It is not a Standard MIDI File, it is cut short, or it measures time in frames of film rather than in
    /// divisions of a quarter note, which this library does not read.
    /// </exception>
    internal static MidiScore Read(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        if (bytes.Length < 14)
        {
            throw new ArgumentException(
                "A Standard MIDI File is at least fourteen bytes and this one is "
                + bytes.Length.ToString(CultureInfo.InvariantCulture) + ".",
                nameof(bytes));
        }

        if (bytes[0] != 'M' || bytes[1] != 'T' || bytes[2] != 'h' || bytes[3] != 'd')
        {
            throw new ArgumentException(
                "A Standard MIDI File starts with 'MThd' and this one starts with '" + Printable(bytes, 0, 4)
                + "'.",
                nameof(bytes));
        }

        long headerLength = BigEndian32(bytes, 4);
        if (headerLength < 6)
        {
            throw new ArgumentException(
                "The header chunk states a length of " + headerLength.ToString(CultureInfo.InvariantCulture)
                + " and it holds at least six bytes.",
                nameof(bytes));
        }

        int division = (bytes[12] << 8) | bytes[13];
        if ((division & 0x8000) != 0 || division == 0)
        {
            throw new ArgumentException(
                "This file measures time in frames of film rather than in divisions of a quarter note, which"
                + " this library does not read.",
                nameof(bytes));
        }

        int at = 8 + (int)headerLength;
        List<MidiEvent> events = new List<MidiEvent>();
        int track = 0;

        while (at + 8 <= bytes.Length)
        {
            long length = BigEndian32(bytes, at + 4);
            if (length < 0 || at + 8 + length > bytes.Length)
            {
                throw new ArgumentException(
                    "The chunk at byte " + at.ToString(CultureInfo.InvariantCulture) + " states a length of "
                    + length.ToString(CultureInfo.InvariantCulture) + " and the file has only "
                    + (bytes.Length - at - 8).ToString(CultureInfo.InvariantCulture) + " bytes left.",
                    nameof(bytes));
            }

            if (bytes[at] == 'M' && bytes[at + 1] == 'T' && bytes[at + 2] == 'r' && bytes[at + 3] == 'k')
            {
                ReadTrack(bytes, at + 8, at + 8 + (int)length, track, events);
                track++;
            }

            //A chunk this library does not know is skipped by the length it states, which is what the format
            //asks a reader to do.
            at += 8 + (int)length;
        }

        return new MidiScore(division, events);
    }

    private static void ReadTrack(byte[] bytes, int from, int to, int track, List<MidiEvent> events)
    {
        Dictionary<int, Queue<MidiSoundingNote>> sounding = new Dictionary<int, Queue<MidiSoundingNote>>();
        List<MidiSoundingNote> order = new List<MidiSoundingNote>();

        int at = from;
        long tick = 0;
        int status = -1;

        while (at < to)
        {
            tick += ReadVariableLengthQuantity(bytes, ref at, to);
            if (at >= to) break;

            int first = bytes[at];
            if (first >= 0x80)
            {
                at++;
                if (first < 0xF0) status = first;
                else status = -1;
            }
            else if (status < 0)
            {
                throw new ArgumentException(
                    "The track at byte " + from.ToString(CultureInfo.InvariantCulture) + " begins an event"
                    + " with " + first.ToString(CultureInfo.InvariantCulture) + ", which can only follow a"
                    + " channel message it takes its status from, and none has come.",
                    nameof(bytes));
            }
            else
            {
                first = status;
            }

            if (first == 0xFF)
            {
                int type = Byte(bytes, ref at, to);
                long length = ReadVariableLengthQuantity(bytes, ref at, to);
                int start = at;
                Advance(ref at, length, to);

                if (type == 0x2F) break;
                ReadMeta(bytes, type, start, length, tick, track, events);
                continue;
            }

            if (first == 0xF0 || first == 0xF7)
            {
                long length = ReadVariableLengthQuantity(bytes, ref at, to);
                Advance(ref at, length, to);
                continue;
            }

            int kind = first & 0xF0;
            int channel = first & 0x0F;

            switch (kind)
            {
                case 0x80:
                {
                    int note = Byte(bytes, ref at, to) & 0x7F;
                    Byte(bytes, ref at, to);
                    Stop(sounding, channel, note, tick);
                    break;
                }

                case 0x90:
                {
                    int note = Byte(bytes, ref at, to) & 0x7F;
                    int velocity = Byte(bytes, ref at, to) & 0x7F;
                    if (velocity == 0)
                    {
                        Stop(sounding, channel, note, tick);
                    }
                    else
                    {
                        MidiSoundingNote started = new MidiSoundingNote(tick, channel, note, velocity);
                        Queue(sounding, channel, note).Enqueue(started);
                        order.Add(started);
                    }

                    break;
                }

                case 0xB0:
                {
                    int controller = Byte(bytes, ref at, to);
                    int value = Byte(bytes, ref at, to);
                    events.Add(MidiEvent.ControlChange(tick, track, channel, controller & 0x7F, value & 0x7F));
                    break;
                }

                case 0xC0:
                {
                    int program = Byte(bytes, ref at, to);
                    events.Add(MidiEvent.ProgramChange(tick, track, channel, program & 0x7F));
                    break;
                }

                case 0xD0:
                    Byte(bytes, ref at, to);
                    break;

                case 0xA0:
                case 0xE0:
                    Byte(bytes, ref at, to);
                    Byte(bytes, ref at, to);
                    break;

                default:
                    throw new ArgumentException(
                        "The track at byte " + from.ToString(CultureInfo.InvariantCulture) + " holds a message"
                        + " beginning with " + first.ToString(CultureInfo.InvariantCulture)
                        + ", which is not a message this format defines.",
                        nameof(bytes));
            }
        }

        //A note the track never ended stops where the track does.
        foreach (MidiSoundingNote note in order)
        {
            if (note.Ends < 0) note.Ends = tick;
            if (note.Ends > note.Tick)
            {
                events.Add(MidiEvent.Note(
                    note.Tick, track, note.Channel, note.Note, note.Velocity, note.Ends - note.Tick));
            }
        }
    }

    private static void ReadMeta(
        byte[] bytes, int type, int start, long length, long tick, int track, List<MidiEvent> events)
    {
        switch (type)
        {
            case 0x51 when length == 3:
                events.Add(MidiEvent.TempoFromMicroseconds(
                    tick, track, (bytes[start] << 16) | (bytes[start + 1] << 8) | bytes[start + 2]));
                break;

            case 0x58 when length >= 2:
            {
                int numerator = bytes[start];
                int power = bytes[start + 1];
                if (numerator >= 1 && power <= 7)
                {
                    events.Add(MidiEvent.TimeSignature(tick, track, numerator, 1 << power));
                }

                break;
            }

            case 0x59 when length >= 2:
            {
                int sharpsOrFlats = (sbyte)bytes[start];
                if (sharpsOrFlats >= -7 && sharpsOrFlats <= 7)
                {
                    events.Add(MidiEvent.KeySignature(tick, track, sharpsOrFlats, bytes[start + 1] != 0));
                }

                break;
            }
        }
    }

    private static Queue<MidiSoundingNote> Queue(
        Dictionary<int, Queue<MidiSoundingNote>> sounding, int channel, int note)
    {
        int key = (channel << 8) | note;
        if (!sounding.TryGetValue(key, out Queue<MidiSoundingNote> queue))
        {
            queue = new Queue<MidiSoundingNote>();
            sounding[key] = queue;
        }

        return queue;
    }

    private static void Stop(
        Dictionary<int, Queue<MidiSoundingNote>> sounding, int channel, int note, long tick)
    {
        int key = (channel << 8) | note;
        if (sounding.TryGetValue(key, out Queue<MidiSoundingNote> queue) && queue.Count > 0)
        {
            queue.Dequeue().Ends = tick;
        }
    }

    /// <summary>Reads a variable-length quantity: seven bits a byte, high bit set but on the last.</summary>
    /// <param name="bytes">The file.</param>
    /// <param name="at">Where to read from; it is moved past what was read.</param>
    /// <param name="to">Where the track chunk ends.</param>
    /// <returns>The number.</returns>
    /// <exception cref="ArgumentException">It runs past the end of the chunk, or takes more than four bytes.</exception>
    internal static long ReadVariableLengthQuantity(byte[] bytes, ref int at, int to)
    {
        long value = 0;
        for (int i = 0; i < 4; i++)
        {
            int one = Byte(bytes, ref at, to);
            value = (value << 7) | (long)(one & 0x7F);
            if ((one & 0x80) == 0) return value;
        }

        throw new ArgumentException(
            "A variable-length quantity is at most four bytes and the one at byte "
            + (at - 4).ToString(CultureInfo.InvariantCulture) + " is longer.",
            nameof(bytes));
    }

    private static int Byte(byte[] bytes, ref int at, int to)
    {
        if (at >= to)
        {
            throw new ArgumentException(
                "The file ends in the middle of an event, at byte " + at.ToString(CultureInfo.InvariantCulture)
                + ".",
                nameof(bytes));
        }

        return bytes[at++];
    }

    private static void Advance(ref int at, long length, int to)
    {
        if (length < 0 || at + length > to)
        {
            throw new ArgumentException(
                "An event at byte " + at.ToString(CultureInfo.InvariantCulture) + " states a length of "
                + length.ToString(CultureInfo.InvariantCulture) + " and its track has only "
                + (to - at).ToString(CultureInfo.InvariantCulture) + " bytes left.");
        }

        at += (int)length;
    }

    private static long BigEndian32(byte[] bytes, int at) =>
        ((long)bytes[at] << 24) | ((long)bytes[at + 1] << 16) | ((long)bytes[at + 2] << 8) | bytes[at + 3];

    private static string Printable(byte[] bytes, int at, int count)
    {
        char[] text = new char[count];
        for (int i = 0; i < count; i++)
        {
            byte one = bytes[at + i];
            text[i] = one >= 32 && one < 127 ? (char)one : '?';
        }

        return new string(text);
    }
}

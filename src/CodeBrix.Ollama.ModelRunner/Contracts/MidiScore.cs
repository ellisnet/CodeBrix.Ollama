using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A whole piece of music as events with absolute positions: the size of a tick, and every
/// <see cref="MidiEvent"/> in it. It is what <see cref="MidiFile"/> reads and writes, and what
/// <see cref="IMidiGenerationModel.ToScore"/> turns a generated stream into.
/// </summary>
/// <remarks>
/// <para>
/// THE EVENTS ARE SORTED as the score is built: by position first, and at one position note endings take
/// effect before anything else and note beginnings last, so a note that ends exactly where the next one of the
/// same pitch begins does not cut it short.
/// </para>
/// <para>
/// A score is NOT a Standard MIDI File and carries no file structure of its own - no running status, no delta
/// times, no track chunks. Those belong to <see cref="MidiFile"/>, which writes this out and reads it back.
/// </para>
/// </remarks>
public sealed class MidiScore
{
    private readonly MidiEvent[] _events;

    /// <summary>Creates a score.</summary>
    /// <param name="ticksPerQuarterNote">
    /// How many ticks a quarter note lasts; 1 to 32767, which is the range a Standard MIDI File can hold.
    /// </param>
    /// <param name="events">
    /// The events. They are copied, and the copy is sorted; the caller's collection is left alone.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An event is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticksPerQuarterNote"/> is outside 1 to 32767.</exception>
    public MidiScore(int ticksPerQuarterNote, IEnumerable<MidiEvent> events)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));
        if (ticksPerQuarterNote < 1 || ticksPerQuarterNote > 32767)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticksPerQuarterNote), ticksPerQuarterNote,
                "A Standard MIDI File holds the ticks per quarter note in fifteen bits, so it is 1 to 32767.");
        }

        List<MidiEvent> copy = new List<MidiEvent>();
        foreach (MidiEvent item in events)
        {
            if (item == null)
            {
                throw new ArgumentException("A score cannot hold an event that is not there.", nameof(events));
            }

            copy.Add(item);
        }

        // A stable sort, so that two events a model placed at the same position in the same track stay in the
        // order it produced them.
        MidiEvent[] sorted = copy.ToArray();
        int[] order = new int[sorted.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (left, right) => Compare(sorted[left], sorted[right], left, right));

        _events = new MidiEvent[sorted.Length];
        for (int i = 0; i < order.Length; i++) _events[i] = sorted[order[i]];

        TicksPerQuarterNote = ticksPerQuarterNote;

        int tracks = 0;
        long length = 0;
        foreach (MidiEvent item in _events)
        {
            if (item.Track + 1 > tracks) tracks = item.Track + 1;
            long ends = item.Tick + item.DurationTicks;
            if (ends > length) length = ends;
        }

        TrackCount = tracks;
        LengthInTicks = length;
    }

    /// <summary>How many ticks a quarter note lasts, which is what turns a tick into a length of time.</summary>
    public int TicksPerQuarterNote { get; }

    /// <summary>Every event, sorted by position.</summary>
    public IReadOnlyList<MidiEvent> Events => _events;

    /// <summary>
    /// How many tracks the piece uses: one more than the largest <see cref="MidiEvent.Track"/> in it, so a
    /// track nothing was written to is still counted.
    /// </summary>
    public int TrackCount { get; }

    /// <summary>Where the piece ends: the last tick any note is still sounding at.</summary>
    public long LengthInTicks { get; }

    /// <summary>
    /// How long the piece lasts in seconds, following its own tempo changes from beginning to end.
    /// </summary>
    /// <remarks>
    /// Until the first tempo event the piece runs at 120 beats per minute, which is what the Standard MIDI
    /// File specification says an unmarked file means.
    /// </remarks>
    /// <returns>The length in seconds.</returns>
    public double DurationInSeconds()
    {
        const double DefaultMicrosecondsPerQuarterNote = 500000.0;

        double seconds = 0;
        long at = 0;
        double microseconds = DefaultMicrosecondsPerQuarterNote;

        foreach (MidiEvent item in _events)
        {
            if (item.Kind != MidiEventKind.Tempo) continue;
            if (item.Tick > at)
            {
                seconds += (item.Tick - at) * microseconds / (TicksPerQuarterNote * 1000000.0);
                at = item.Tick;
            }

            microseconds = item.MicrosecondsPerQuarterNote;
        }

        if (LengthInTicks > at)
        {
            seconds += (LengthInTicks - at) * microseconds / (TicksPerQuarterNote * 1000000.0);
        }

        return seconds;
    }

    private static int Compare(MidiEvent left, MidiEvent right, int leftIndex, int rightIndex)
    {
        int byTick = left.Tick.CompareTo(right.Tick);
        if (byTick != 0) return byTick;

        int byPhase = Phase(left).CompareTo(Phase(right));
        if (byPhase != 0) return byPhase;

        return leftIndex.CompareTo(rightIndex);
    }

    //At one position, everything that SETS SOMETHING UP - an instrument, a controller, a tempo, a signature -
    //takes effect before a note sounds, so a note is never played on the instrument that was there a moment
    //ago. Where a note ENDS is not a score event at all: a note carries its own length, and the file writer is
    //what turns that into an ending in the right place.
    private static int Phase(MidiEvent item) => item.Kind == MidiEventKind.Note ? 2 : 1;
}

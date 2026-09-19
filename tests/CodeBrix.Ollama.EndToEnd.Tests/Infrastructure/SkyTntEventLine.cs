using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CodeBrix.Ollama.ModelRunner;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// Turns the events the managed driver hands over back into the form the publisher's own loop reports them
/// in, so that the two can be compared parameter for parameter.
/// </summary>
/// <remarks>
/// <para>
/// THE PUBLIC EVENT CARRIES MORE THAN THE MODEL WROTE, not less: an absolute position instead of a distance
/// from the last event, a length in ticks instead of in sixteenths of a beat, a time signature written the way
/// it is read instead of as a power of two. Every one of those has exactly one way back, so writing them back
/// out compares the same numbers the model actually chose - which is what "the same, token for token" means
/// here.
/// </para>
/// <para>
/// The one exception is the tempo, which is compared as the MICROSECONDS a file holds: the model writes whole
/// beats a minute, the conversion to microseconds truncates, and going back the other way would have to guess
/// what was truncated.
/// </para>
/// </remarks>
public static class SkyTntEventLine
{
    /// <summary>How many ticks a quarter note lasts in everything the model produces.</summary>
    public const int TicksPerQuarterNote = 480;

    /// <summary>How many parts of a beat the model places an event on.</summary>
    public const int StepsPerBeat = 16;

    /// <summary>
    /// Writes the events out as one line each, in the publisher's own form.
    /// </summary>
    /// <param name="events">The events, in the order they were handed over.</param>
    /// <param name="fromBeat">
    /// How far into the piece the prompt already reached, in whole beats - which is what the first event's
    /// distance is measured from.
    /// </param>
    /// <returns>One line per event.</returns>
    public static IReadOnlyList<string> Lines(IReadOnlyList<MidiEvent> events, int fromBeat)
    {
        List<string> lines = new List<string>(events.Count);
        int last = fromBeat;

        foreach (MidiEvent item in events)
        {
            int beat = (int)(item.Tick / TicksPerQuarterNote);
            int withinBeat = (int)((item.Tick % TicksPerQuarterNote) / (TicksPerQuarterNote / StepsPerBeat));
            lines.Add(Line(item, beat - last, withinBeat));
            last = beat;
        }

        return lines;
    }

    private static string Line(MidiEvent item, int distance, int withinBeat)
    {
        StringBuilder text = new StringBuilder();
        text.Append(Name(item.Kind));
        Append(text, distance);
        Append(text, withinBeat);
        Append(text, item.Track);

        switch (item.Kind)
        {
            case MidiEventKind.Note:
                Append(text, item.Channel);
                Append(text, item.NoteNumber);
                Append(text, item.Velocity);
                Append(text, (int)(item.DurationTicks / (TicksPerQuarterNote / StepsPerBeat)));
                break;

            case MidiEventKind.ProgramChange:
                Append(text, item.Channel);
                Append(text, item.Program);
                break;

            case MidiEventKind.ControlChange:
                Append(text, item.Channel);
                Append(text, item.Controller);
                Append(text, item.Value);
                break;

            case MidiEventKind.Tempo:
                text.Append(' ');
                text.Append(item.MicrosecondsPerQuarterNote.ToString(CultureInfo.InvariantCulture));
                break;

            case MidiEventKind.TimeSignature:
                Append(text, item.Numerator - 1);
                Append(text, Power(item.Denominator) - 1);
                break;

            case MidiEventKind.KeySignature:
                Append(text, item.SharpsOrFlats + 7);
                Append(text, item.IsMinor ? 1 : 0);
                break;
        }

        return text.ToString();
    }

    private static string Name(MidiEventKind kind) => kind switch
    {
        MidiEventKind.Note => "note",
        MidiEventKind.ProgramChange => "patch_change",
        MidiEventKind.ControlChange => "control_change",
        MidiEventKind.Tempo => "set_tempo",
        MidiEventKind.TimeSignature => "time_signature",
        _ => "key_signature",
    };

    private static void Append(StringBuilder text, int value)
    {
        text.Append(' ');
        text.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static int Power(int denominator)
    {
        int power = 0;
        int value = denominator;
        while (value > 1)
        {
            value >>= 1;
            power++;
        }

        return power;
    }
}

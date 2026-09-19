using System;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One musical event, ready to play: a note with a duration, an instrument change, a controller change, or a
/// change of tempo, time signature or key signature - each at an ABSOLUTE position in ticks from the start of
/// the piece.
/// </summary>
/// <remarks>
/// <para>
/// IT IS STREAMING-READY, and that is the reason it carries absolute time rather than the model's own
/// encoding. A generator yields one of these as soon as its last token is sampled, so a player can be fed
/// while the rest of the piece is still being generated. Everything a player needs is here: the position
/// (<see cref="Tick"/>), the length of a note (<see cref="DurationTicks"/>), and - through
/// <see cref="HorizonTicks"/> - how far ahead it is safe to play before a later event could still arrive.
/// </para>
/// <para>
/// TICKS ARE COUNTED FROM THE START OF THE PIECE and their size is stated separately, by the
/// <see cref="MidiScore.TicksPerQuarterNote"/> of the score the event belongs to or by
/// <see cref="MidiGenerationMetadata.TicksPerQuarterNote"/> of the model that produced it. An event does not
/// carry the resolution itself, because every event of one piece shares it.
/// </para>
/// <para>
/// CHANNELS ARE NUMBERED 0 TO 15, which is how the MIDI wire format numbers them and how a model of this
/// family emits them; channel 9 is the percussion channel. A player that numbers channels 1 to 16 wants
/// <see cref="Channel"/> PLUS ONE. Kinds that belong to no channel
/// (<see cref="MidiEventKind.Tempo"/>, <see cref="MidiEventKind.TimeSignature"/> and
/// <see cref="MidiEventKind.KeySignature"/>) carry <see cref="NoChannel"/>.
/// </para>
/// <para>
/// TEMPO IS IN BEATS PER MINUTE, where a beat is a quarter note. <see cref="MicrosecondsPerQuarterNote"/>
/// carries the same thing in the unit a Standard MIDI File stores, which is what the file's own byte is
/// written from.
/// </para>
/// <para>
/// PROPERTIES THAT DO NOT APPLY TO THE KIND ARE NOUGHT (and <see cref="Channel"/> is <see cref="NoChannel"/>).
/// Switch on <see cref="Kind"/> and read the ones that belong to it.
/// </para>
/// </remarks>
public sealed class MidiEvent
{
    /// <summary>What <see cref="Channel"/> holds for a kind that belongs to no channel.</summary>
    public const int NoChannel = -1;

    private MidiEvent(MidiEventKind kind, long tick, long horizonTicks, int track, int channel)
    {
        Kind = kind;
        Tick = tick;
        HorizonTicks = horizonTicks;
        Track = track;
        Channel = channel;
    }

    /// <summary>Which of the six kinds of event this is; it decides which properties below are meaningful.</summary>
    public MidiEventKind Kind { get; }

    /// <summary>
    /// When it happens: ticks from the start of the piece, where a quarter note is the score's or the model's
    /// stated number of ticks.
    /// </summary>
    public long Tick { get; }

    /// <summary>
    /// How far the piece is settled when this event is yielded: NO EVENT YIELDED AFTER THIS ONE WILL HAVE A
    /// <see cref="Tick"/> BELOW THIS NUMBER. A player streaming a piece that is still being generated can
    /// sound everything up to here and nothing beyond it.
    /// </summary>
    /// <remarks>
    /// It is never above <see cref="Tick"/>, and it is usually below it: a model of this family places each
    /// event at a whole beat plus an offset inside that beat, and the beat only ever moves forward, so the
    /// horizon an event carries is the start of the beat it sits in. An event that was not produced by a
    /// generator - one read from a file, or built by hand - carries its own tick here.
    /// </remarks>
    public long HorizonTicks { get; }

    /// <summary>
    /// Which track of the piece it belongs to, counted from nought. A model of this family writes one track
    /// per instrumental part and puts its tempo and time-signature events on track nought.
    /// </summary>
    public int Track { get; }

    /// <summary>
    /// Which MIDI channel it is on, 0 to 15, where 9 is percussion - or <see cref="NoChannel"/> for a kind
    /// that belongs to no channel. Add one for an interface that numbers channels 1 to 16.
    /// </summary>
    public int Channel { get; }

    /// <summary>The pitch of a <see cref="MidiEventKind.Note"/>, 0 to 127, where 60 is middle C.</summary>
    public int NoteNumber { get; private init; }

    /// <summary>How hard a <see cref="MidiEventKind.Note"/> is struck, 1 to 127.</summary>
    public int Velocity { get; private init; }

    /// <summary>How long a <see cref="MidiEventKind.Note"/> sounds, in ticks. It is always above nought.</summary>
    public long DurationTicks { get; private init; }

    /// <summary>The instrument a <see cref="MidiEventKind.ProgramChange"/> selects, 0 to 127.</summary>
    public int Program { get; private init; }

    /// <summary>Which controller a <see cref="MidiEventKind.ControlChange"/> sets, 0 to 127.</summary>
    public int Controller { get; private init; }

    /// <summary>What a <see cref="MidiEventKind.ControlChange"/> sets its controller to, 0 to 127.</summary>
    public int Value { get; private init; }

    /// <summary>The speed a <see cref="MidiEventKind.Tempo"/> sets, in quarter notes per minute.</summary>
    public double BeatsPerMinute { get; private init; }

    /// <summary>
    /// The same speed as <see cref="BeatsPerMinute"/>, in the microseconds-per-quarter-note unit a Standard
    /// MIDI File stores. It is what the file's own bytes are written from and read into.
    /// </summary>
    public long MicrosecondsPerQuarterNote { get; private init; }

    /// <summary>How many beats are in a bar, for a <see cref="MidiEventKind.TimeSignature"/> - the 3 of 3/4.</summary>
    public int Numerator { get; private init; }

    /// <summary>
    /// What kind of note gets one beat, for a <see cref="MidiEventKind.TimeSignature"/> - the 4 of 3/4. It is
    /// always a power of two.
    /// </summary>
    public int Denominator { get; private init; }

    /// <summary>
    /// How many sharps (above nought) or flats (below nought) a <see cref="MidiEventKind.KeySignature"/> has,
    /// -7 to 7.
    /// </summary>
    public int SharpsOrFlats { get; private init; }

    /// <summary>Whether a <see cref="MidiEventKind.KeySignature"/> is a minor key rather than a major one.</summary>
    public bool IsMinor { get; private init; }

    /// <summary>Builds a note.</summary>
    /// <param name="tick">Ticks from the start of the piece.</param>
    /// <param name="track">The track it belongs to, from nought.</param>
    /// <param name="channel">The MIDI channel, 0 to 15, where 9 is percussion.</param>
    /// <param name="noteNumber">The pitch, 0 to 127.</param>
    /// <param name="velocity">How hard it is struck, 1 to 127.</param>
    /// <param name="durationTicks">How long it sounds, in ticks; above nought.</param>
    /// <returns>The event.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside the range stated for it.</exception>
    public static MidiEvent Note(
        long tick, int track, int channel, int noteNumber, int velocity, long durationTicks)
    {
        RequireTick(tick);
        RequireTrack(track);
        RequireChannel(channel);
        RequireSevenBit(noteNumber, nameof(noteNumber));
        RequireSevenBit(velocity, nameof(velocity));
        if (durationTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationTicks), durationTicks, "A note must sound for at least one tick.");
        }

        return new MidiEvent(MidiEventKind.Note, tick, tick, track, channel)
        {
            NoteNumber = noteNumber,
            Velocity = velocity,
            DurationTicks = durationTicks,
        };
    }

    /// <summary>Builds an instrument change.</summary>
    /// <param name="tick">Ticks from the start of the piece.</param>
    /// <param name="track">The track it belongs to, from nought.</param>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="program">The instrument, 0 to 127.</param>
    /// <returns>The event.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside the range stated for it.</exception>
    public static MidiEvent ProgramChange(long tick, int track, int channel, int program)
    {
        RequireTick(tick);
        RequireTrack(track);
        RequireChannel(channel);
        RequireSevenBit(program, nameof(program));

        return new MidiEvent(MidiEventKind.ProgramChange, tick, tick, track, channel) { Program = program };
    }

    /// <summary>Builds a controller change.</summary>
    /// <param name="tick">Ticks from the start of the piece.</param>
    /// <param name="track">The track it belongs to, from nought.</param>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="controller">Which controller, 0 to 127.</param>
    /// <param name="value">What to set it to, 0 to 127.</param>
    /// <returns>The event.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside the range stated for it.</exception>
    public static MidiEvent ControlChange(long tick, int track, int channel, int controller, int value)
    {
        RequireTick(tick);
        RequireTrack(track);
        RequireChannel(channel);
        RequireSevenBit(controller, nameof(controller));
        RequireSevenBit(value, nameof(value));

        return new MidiEvent(MidiEventKind.ControlChange, tick, tick, track, channel)
        {
            Controller = controller,
            Value = value,
        };
    }

    /// <summary>Builds a change of speed from a number of beats per minute.</summary>
    /// <param name="tick">Ticks from the start of the piece.</param>
    /// <param name="track">The track it belongs to, from nought.</param>
    /// <param name="beatsPerMinute">Quarter notes per minute; above nought.</param>
    /// <returns>The event.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside the range stated for it.</exception>
    public static MidiEvent Tempo(long tick, int track, double beatsPerMinute)
    {
        RequireTick(tick);
        RequireTrack(track);
        if (!(beatsPerMinute > 0) || double.IsInfinity(beatsPerMinute))
        {
            throw new ArgumentOutOfRangeException(
                nameof(beatsPerMinute), beatsPerMinute, "A tempo must be above nought.");
        }

        //The same rounding the model's own tokenizer uses to turn a tempo in beats per minute into the
        //microseconds a file stores: the quotient is truncated, not rounded.
        long microseconds = (long)((60.0 / beatsPerMinute) * 1000000.0);
        if (microseconds < 1) microseconds = 1;

        return new MidiEvent(MidiEventKind.Tempo, tick, tick, track, NoChannel)
        {
            BeatsPerMinute = beatsPerMinute,
            MicrosecondsPerQuarterNote = microseconds,
        };
    }

    /// <summary>
    /// Builds a change of speed from the microseconds-per-quarter-note a Standard MIDI File stores, which is
    /// what a file read gives and what keeps a read-and-write round trip exact.
    /// </summary>
    /// <param name="tick">Ticks from the start of the piece.</param>
    /// <param name="track">The track it belongs to, from nought.</param>
    /// <param name="microsecondsPerQuarterNote">Microseconds per quarter note; 1 to 16,777,215.</param>
    /// <returns>The event.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside the range stated for it.</exception>
    public static MidiEvent TempoFromMicroseconds(long tick, int track, long microsecondsPerQuarterNote)
    {
        RequireTick(tick);
        RequireTrack(track);
        if (microsecondsPerQuarterNote < 1 || microsecondsPerQuarterNote > 0xFFFFFF)
        {
            throw new ArgumentOutOfRangeException(
                nameof(microsecondsPerQuarterNote), microsecondsPerQuarterNote,
                "A Standard MIDI File holds the microseconds per quarter note in three bytes, so it is 1 to"
                + " 16777215.");
        }

        return new MidiEvent(MidiEventKind.Tempo, tick, tick, track, NoChannel)
        {
            BeatsPerMinute = 60000000.0 / microsecondsPerQuarterNote,
            MicrosecondsPerQuarterNote = microsecondsPerQuarterNote,
        };
    }

    /// <summary>Builds a change of time signature.</summary>
    /// <param name="tick">Ticks from the start of the piece.</param>
    /// <param name="track">The track it belongs to, from nought.</param>
    /// <param name="numerator">How many beats are in a bar, 1 to 255.</param>
    /// <param name="denominator">What note gets a beat: a power of two, 1 to 128.</param>
    /// <returns>The event.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside the range stated for it.</exception>
    public static MidiEvent TimeSignature(long tick, int track, int numerator, int denominator)
    {
        RequireTick(tick);
        RequireTrack(track);
        if (numerator < 1 || numerator > 255)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numerator), numerator, "A time signature's upper number is 1 to 255.");
        }

        if (denominator < 1 || denominator > 128 || (denominator & (denominator - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(denominator), denominator,
                "A time signature's lower number is a power of two from 1 to 128, because a file stores it as"
                + " the power itself.");
        }

        return new MidiEvent(MidiEventKind.TimeSignature, tick, tick, track, NoChannel)
        {
            Numerator = numerator,
            Denominator = denominator,
        };
    }

    /// <summary>Builds a change of key signature.</summary>
    /// <param name="tick">Ticks from the start of the piece.</param>
    /// <param name="track">The track it belongs to, from nought.</param>
    /// <param name="sharpsOrFlats">Sharps above nought, flats below it, -7 to 7.</param>
    /// <param name="isMinor">Whether it is a minor key.</param>
    /// <returns>The event.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside the range stated for it.</exception>
    public static MidiEvent KeySignature(long tick, int track, int sharpsOrFlats, bool isMinor)
    {
        RequireTick(tick);
        RequireTrack(track);
        if (sharpsOrFlats < -7 || sharpsOrFlats > 7)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sharpsOrFlats), sharpsOrFlats, "A key signature has -7 to 7 sharps or flats.");
        }

        return new MidiEvent(MidiEventKind.KeySignature, tick, tick, track, NoChannel)
        {
            SharpsOrFlats = sharpsOrFlats,
            IsMinor = isMinor,
        };
    }

    /// <summary>Describes the event in one line, for diagnostics and test output.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        StringBuilder text = new StringBuilder();
        text.Append(Kind.ToString());
        text.Append(" at ");
        text.Append(Tick.ToString(CultureInfo.InvariantCulture));
        text.Append(" track ");
        text.Append(Track.ToString(CultureInfo.InvariantCulture));
        if (Channel != NoChannel)
        {
            text.Append(" channel ");
            text.Append(Channel.ToString(CultureInfo.InvariantCulture));
        }

        switch (Kind)
        {
            case MidiEventKind.Note:
                text.Append(" note ");
                text.Append(NoteNumber.ToString(CultureInfo.InvariantCulture));
                text.Append(" velocity ");
                text.Append(Velocity.ToString(CultureInfo.InvariantCulture));
                text.Append(" for ");
                text.Append(DurationTicks.ToString(CultureInfo.InvariantCulture));
                break;
            case MidiEventKind.ProgramChange:
                text.Append(" program ");
                text.Append(Program.ToString(CultureInfo.InvariantCulture));
                break;
            case MidiEventKind.ControlChange:
                text.Append(" controller ");
                text.Append(Controller.ToString(CultureInfo.InvariantCulture));
                text.Append('=');
                text.Append(Value.ToString(CultureInfo.InvariantCulture));
                break;
            case MidiEventKind.Tempo:
                text.Append(' ');
                text.Append(BeatsPerMinute.ToString("F3", CultureInfo.InvariantCulture));
                text.Append(" bpm");
                break;
            case MidiEventKind.TimeSignature:
                text.Append(' ');
                text.Append(Numerator.ToString(CultureInfo.InvariantCulture));
                text.Append('/');
                text.Append(Denominator.ToString(CultureInfo.InvariantCulture));
                break;
            case MidiEventKind.KeySignature:
                text.Append(' ');
                text.Append(SharpsOrFlats.ToString(CultureInfo.InvariantCulture));
                text.Append(IsMinor ? " minor" : " major");
                break;
        }

        return text.ToString();
    }

    /// <summary>
    /// The same event with a horizon of its own, which only a generator sets: it knows what the model can
    /// still place before this event's position and a caller does not.
    /// </summary>
    /// <param name="horizonTicks">The horizon, which is never above <see cref="Tick"/>.</param>
    /// <returns>A copy carrying that horizon.</returns>
    internal MidiEvent WithHorizon(long horizonTicks) =>
        new MidiEvent(Kind, Tick, horizonTicks > Tick ? Tick : horizonTicks, Track, Channel)
        {
            NoteNumber = NoteNumber,
            Velocity = Velocity,
            DurationTicks = DurationTicks,
            Program = Program,
            Controller = Controller,
            Value = Value,
            BeatsPerMinute = BeatsPerMinute,
            MicrosecondsPerQuarterNote = MicrosecondsPerQuarterNote,
            Numerator = Numerator,
            Denominator = Denominator,
            SharpsOrFlats = SharpsOrFlats,
            IsMinor = IsMinor,
        };

    /// <summary>The same note with a different duration, which is how an overlap is trimmed.</summary>
    /// <param name="durationTicks">The new duration, above nought.</param>
    /// <returns>A copy sounding for that long.</returns>
    internal MidiEvent WithDuration(long durationTicks) =>
        new MidiEvent(Kind, Tick, HorizonTicks, Track, Channel)
        {
            NoteNumber = NoteNumber,
            Velocity = Velocity,
            DurationTicks = durationTicks,
        };

    private static void RequireTick(long tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "A position cannot be before the start.");
        }
    }

    private static void RequireTrack(int track)
    {
        if (track < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(track), track, "A track is counted from nought.");
        }
    }

    private static void RequireChannel(int channel)
    {
        if (channel < 0 || channel > 15)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel), channel, "A MIDI channel is 0 to 15, where 9 is percussion.");
        }
    }

    private static void RequireSevenBit(int value, string name)
    {
        if (value < 0 || value > 127)
        {
            throw new ArgumentOutOfRangeException(name, value, "A MIDI value is 0 to 127.");
        }
    }
}

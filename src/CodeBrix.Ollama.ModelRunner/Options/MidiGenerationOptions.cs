using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What to generate and how: how much music, how adventurous the model is allowed to be, what to start it off
/// with, and what to refuse it.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFAULTS GENERATE MUSIC. A caller that sets nothing gets five hundred and twelve events with the
/// publisher's own sampling settings and a seed of its own, starting from nothing but "here comes a piece of
/// music" - which is how the model is meant to be asked for a piece with no further instructions.
/// </para>
/// <para>
/// THERE ARE TWO WAYS TO START IT OFF AND THEY ARE ALTERNATIVES. Either say what the piece should be made of
/// - which instruments, how fast, what time and key signature - or hand it a piece of music to CONTINUE
/// through <see cref="Prompt"/>. Setting both is refused rather than quietly resolved, because there is no
/// sensible way to mean both at once.
/// </para>
/// <para>
/// HOW MANY THREADS a generation spreads its arithmetic over is settled when the model is LOADED, not here:
/// it belongs to the loaded graphs. Pass an <see cref="OnnxRunnerOptions"/> with a thread count to
/// <see cref="MidiGenerationModel"/> when loading.
/// </para>
/// </remarks>
public sealed class MidiGenerationOptions
{
    private long? _seed;

    /// <summary>
    /// How many events to generate. Default 512. A generation may stop sooner, when the model decides the
    /// piece is finished.
    /// </summary>
    /// <remarks>
    /// An event is a note, an instrument change, a controller change, or a change of tempo, time signature or
    /// key signature - so this is a count of musical events and not of seconds. A few hundred events is a
    /// fragment; a few thousand is a piece.
    /// </remarks>
    public int MaximumEvents { get; set; } = 512;

    /// <summary>
    /// How much the model's answer is flattened before a token is drawn from it. Default 1, which leaves the
    /// answer as the model gave it; below one makes the music more predictable and above one less so.
    /// </summary>
    public double Temperature { get; set; } = 1.0;

    /// <summary>
    /// Keep only the most likely tokens, up to this much probability between them. Default 0.98. One keeps
    /// them all.
    /// </summary>
    public double TopP { get; set; } = 0.98;

    /// <summary>
    /// Keep at most this many of the most likely tokens. Default 20. ONE MAKES THE GENERATION GREEDY: the
    /// model's most likely answer is taken every time, no random number is drawn, and the same prompt always
    /// gives the same piece.
    /// </summary>
    public int TopK { get; set; } = 20;

    /// <summary>
    /// What the stream of random numbers starts from. Set to <see langword="null"/> (the default) for a
    /// fresh seed. A fixed seed selects a repeatable random stream; reproducing the piece also requires the
    /// same model, settings, build and hardware, because inference arithmetic may differ between processors.
    /// </summary>
    /// <remarks>
    /// Explicit seeds must be nonnegative. The reserved value 0xFFFFFFFF is rejected consistently with
    /// <see cref="SamplingOptions.Seed"/>. Other nonnegative 64-bit values remain supported for MIDI.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The seed is negative or is 0xFFFFFFFF.</exception>
    public long? Seed
    {
        get => _seed;
        set
        {
            if (value < 0 || value == uint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(Seed), value,
                    "Seed must be nonnegative and cannot be the reserved value 0xFFFFFFFF. Use null for a fresh random seed.");
            _seed = value;
        }
    }

    /// <summary>
    /// Which instruments the piece is for, as General MIDI program numbers (0 to 127). They are given to the
    /// channels in turn, and naming any of them also stops the model choosing instruments of its own or
    /// writing on a channel that was not asked for.
    /// </summary>
    /// <remarks>
    /// The percussion channel is not among them: ask for percussion through <see cref="DrumKit"/>. Leave this
    /// empty to let the model choose the instrumentation itself, which is what it does well.
    /// </remarks>
    public IReadOnlyList<int> Instruments { get; set; }

    /// <summary>
    /// Which drum kit the piece uses, as the program number the percussion channel is set to - 0 for the
    /// standard kit, 8 for the room kit, 16 for the power kit, 24 or 25 for the electronic ones, 32 for jazz,
    /// 40 for brushes, 48 for an orchestral kit. Unset asks for no percussion.
    /// </summary>
    public int? DrumKit { get; set; }

    /// <summary>
    /// How fast the piece is, in quarter notes per minute, 1 to 383. Unset lets the model choose.
    /// </summary>
    public int? BeatsPerMinute { get; set; }

    /// <summary>
    /// The upper number of the time signature to start in, 1 to 16 - the 3 of 3/4. It is set together with
    /// <see cref="TimeSignatureDenominator"/>, and unset lets the model choose.
    /// </summary>
    public int? TimeSignatureNumerator { get; set; }

    /// <summary>
    /// The lower number of the time signature to start in: 2, 4, 8 or 16 - the 4 of 3/4. It is set together
    /// with <see cref="TimeSignatureNumerator"/>.
    /// </summary>
    public int? TimeSignatureDenominator { get; set; }

    /// <summary>
    /// How many sharps (above nought) or flats (below nought) the key signature has, -7 to 7. Unset lets the
    /// model choose.
    /// </summary>
    public int? KeySignatureSharpsOrFlats { get; set; }

    /// <summary>
    /// Whether the key signature asked for is a minor key. It is read only when
    /// <see cref="KeySignatureSharpsOrFlats"/> is set.
    /// </summary>
    public bool KeySignatureIsMinor { get; set; }

    /// <summary>
    /// A piece of music to CONTINUE, instead of describing one to start. The model reads it through its own
    /// tokenizer and carries on from the end of it.
    /// </summary>
    /// <remarks>
    /// Read one with <see cref="MidiFile.ReadAsync"/>. It is an alternative to the instrument, tempo and
    /// signature settings above, and setting both is refused.
    /// </remarks>
    public MidiScore Prompt { get; set; }

    /// <summary>
    /// How many of the prompt's events to keep, counted from the beginning. Default 4096, which is as many as
    /// the model can look back over anyway.
    /// </summary>
    public int PromptEventLimit { get; set; } = 4096;

    /// <summary>
    /// Whether a controller change in the prompt that barely moves the controller is left out, which is what
    /// the model was trained on. Default <see langword="true"/>.
    /// </summary>
    public bool ReduceRepeatedChanges { get; set; } = true;

    /// <summary>
    /// Whether the model may write controller changes - pedals, volume, pan. Default <see langword="true"/>.
    /// Switching it off makes a plainer piece and generates it faster, since those events are not spent.
    /// </summary>
    public bool AllowControlChange { get; set; } = true;

    /// <summary>
    /// Whether the events of the PROMPT are handed to the caller before the generated ones. Default
    /// <see langword="true"/>, which makes the enumeration the whole piece from its first event - including
    /// the instruments, tempo and signatures that were asked for, which the music needs.
    /// </summary>
    public bool IncludePromptEvents { get; set; } = true;

    /// <summary>Makes an independent copy, so that a caller's later edits cannot change a generation.</summary>
    /// <returns>The copy.</returns>
    internal MidiGenerationOptions Copy()
    {
        List<int> instruments = null;
        if (Instruments != null)
        {
            instruments = new List<int>(Instruments.Count);
            foreach (int instrument in Instruments) instruments.Add(instrument);
        }

        return new MidiGenerationOptions
        {
            MaximumEvents = MaximumEvents,
            Temperature = Temperature,
            TopP = TopP,
            TopK = TopK,
            Seed = Seed,
            Instruments = instruments,
            DrumKit = DrumKit,
            BeatsPerMinute = BeatsPerMinute,
            TimeSignatureNumerator = TimeSignatureNumerator,
            TimeSignatureDenominator = TimeSignatureDenominator,
            KeySignatureSharpsOrFlats = KeySignatureSharpsOrFlats,
            KeySignatureIsMinor = KeySignatureIsMinor,
            Prompt = Prompt,
            PromptEventLimit = PromptEventLimit,
            ReduceRepeatedChanges = ReduceRepeatedChanges,
            AllowControlChange = AllowControlChange,
            IncludePromptEvents = IncludePromptEvents,
        };
    }
}

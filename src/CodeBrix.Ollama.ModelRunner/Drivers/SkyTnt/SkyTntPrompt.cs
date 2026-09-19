using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner; //was previously: app_onnx.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// What a generation starts from: the caller's settings turned into the rows of tokens the model reads before
/// it writes anything of its own, and the refusals that go with them.
/// </summary>
/// <remarks>
/// <para>
/// ASKING FOR INSTRUMENTS ALSO CLOSES DOORS, and that is the publisher's own behaviour rather than a choice
/// made here: naming the instruments writes an instrument change for each one AND stops the model both from
/// choosing instruments of its own and from writing on any channel that was not asked for. A piece "for flute
/// and harp" that quietly acquired a drum kit halfway through would not be what was asked for.
/// </para>
/// <para>
/// THE CHANNELS ARE HANDED OUT IN TURN and channel nine is stepped over, because it is the percussion channel
/// wherever it appears; a drum kit takes it. Each instrument change goes on a track of its own.
/// </para>
/// </remarks>
internal static class SkyTntPrompt
{
    /// <summary>The largest speed the model's vocabulary can express, in quarter notes per minute.</summary>
    internal const int MaximumBeatsPerMinute = 383;

    /// <summary>
    /// Settles one generation: checks the caller's options, builds the rows of tokens it starts from, and
    /// works out what the model is refused throughout.
    /// </summary>
    /// <param name="tokenizer">The model's tokenizer.</param>
    /// <param name="options">The caller's options.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">
    /// An option is outside the range stated for it, or a piece of music to continue is given together with
    /// settings describing a piece to start.
    /// </exception>
    internal static SkyTntGenerationPlan Plan(SkyTntTokenizer tokenizer, MidiGenerationOptions options)
    {
        Validate(options);

        List<int[]> rows = new List<int[]>();
        bool[] allowedChannels = new bool[16];
        bool disableProgramChange = false;

        if (options.Prompt != null)
        {
            for (int channel = 0; channel < 16; channel++) allowedChannels[channel] = true;

            int epsilon = options.ReduceRepeatedChanges ? 4 : 0;
            List<int[]> tokenized = tokenizer.Tokenize(options.Prompt, true, epsilon, epsilon);

            //The tokenizer ends a piece with the token that says "and that is the end of it", which is exactly
            //what a model being asked to CONTINUE the piece must not be shown.
            if (tokenized.Count > 0 && tokenized[tokenized.Count - 1][0] == tokenizer.EndId)
            {
                tokenized.RemoveAt(tokenized.Count - 1);
            }

            int keep = Math.Min(options.PromptEventLimit, tokenized.Count);
            for (int i = 0; i < keep; i++) rows.Add(tokenized[i]);

            if (rows.Count == 0) rows.Add(tokenizer.BeginningRow());
            return new SkyTntGenerationPlan(
                rows, options.MaximumEvents, options.Temperature, options.TopP, options.TopK,
                Seed(options), false, !options.AllowControlChange, allowedChannels,
                options.IncludePromptEvents);
        }

        rows.Add(tokenizer.BeginningRow());

        if (options.TimeSignatureNumerator.HasValue)
        {
            Add(tokenizer, rows, "time_signature", new[]
            {
                0, 0, 0, options.TimeSignatureNumerator.Value - 1,
                Power(options.TimeSignatureDenominator.Value) - 1,
            });
        }

        if (options.KeySignatureSharpsOrFlats.HasValue)
        {
            Add(tokenizer, rows, "key_signature", new[]
            {
                0, 0, 0, options.KeySignatureSharpsOrFlats.Value + 7, options.KeySignatureIsMinor ? 1 : 0,
            });
        }

        if (options.BeatsPerMinute.HasValue)
        {
            Add(tokenizer, rows, "set_tempo", new[] { 0, 0, 0, options.BeatsPerMinute.Value });
        }

        List<int> channels = new List<int>();
        List<int> patches = new List<int>();
        int next = 0;
        if (options.Instruments != null)
        {
            foreach (int instrument in options.Instruments)
            {
                channels.Add(next);
                patches.Add(instrument);

                //Channel nine belongs to percussion, so the ninth instrument takes channel ten.
                next = next != 8 ? next + 1 : 10;
            }
        }

        if (options.DrumKit.HasValue)
        {
            channels.Add(9);
            patches.Add(options.DrumKit.Value);
        }

        for (int i = 0; i < channels.Count; i++)
        {
            Add(tokenizer, rows, "patch_change", new[] { 0, 0, i + 1, channels[i], patches[i] });
        }

        if (options.Instruments != null && options.Instruments.Count > 0)
        {
            disableProgramChange = true;
            foreach (int channel in channels) allowedChannels[channel] = true;
        }
        else
        {
            for (int channel = 0; channel < 16; channel++) allowedChannels[channel] = true;
        }

        return new SkyTntGenerationPlan(
            rows, options.MaximumEvents, options.Temperature, options.TopP, options.TopK, Seed(options),
            disableProgramChange, !options.AllowControlChange, allowedChannels, options.IncludePromptEvents);
    }

    private static long Seed(MidiGenerationOptions options) =>
        options.Seed ?? DateTime.UtcNow.Ticks;

    private static void Add(
        SkyTntTokenizer tokenizer, List<int[]> rows, string name, int[] values)
    {
        SkyTntEventType type = tokenizer.TypeForName(name);
        int[] tokens = tokenizer.EventToTokens(new SkyTntEventRow(type, values));
        if (tokens != null) rows.Add(tokens);
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

    private static void Validate(MidiGenerationOptions options)
    {
        if (options.MaximumEvents < 1)
        {
            throw new ArgumentException(
                "A generation makes at least one event and was asked for "
                + options.MaximumEvents.ToString(CultureInfo.InvariantCulture) + ".",
                nameof(options));
        }

        if (!(options.Temperature > 0) || double.IsInfinity(options.Temperature))
        {
            throw new ArgumentException("A temperature is above nought.", nameof(options));
        }

        if (!(options.TopP > 0) || options.TopP > 1)
        {
            throw new ArgumentException(
                "A nucleus threshold is above nought and at most one.", nameof(options));
        }

        if (options.TopK < 1)
        {
            throw new ArgumentException(
                "At least one token has to be kept to choose from; one makes the generation greedy.",
                nameof(options));
        }

        if (options.PromptEventLimit < 1)
        {
            throw new ArgumentException(
                "At least one event of a prompt has to be kept.", nameof(options));
        }

        if (options.Prompt != null && DescribesAPiece(options))
        {
            throw new ArgumentException(
                "A piece of music to continue and a description of a piece to start are alternatives: set"
                + " Prompt, or set the instruments, drum kit, tempo and signatures - not both.",
                nameof(options));
        }

        if (options.Instruments != null)
        {
            foreach (int instrument in options.Instruments) SevenBit(instrument, "An instrument");
        }

        if (options.DrumKit.HasValue) SevenBit(options.DrumKit.Value, "A drum kit");

        if (options.BeatsPerMinute.HasValue
            && (options.BeatsPerMinute.Value < 1 || options.BeatsPerMinute.Value > MaximumBeatsPerMinute))
        {
            throw new ArgumentException(
                "This model writes a speed of 1 to "
                + MaximumBeatsPerMinute.ToString(CultureInfo.InvariantCulture) + " quarter notes per minute.",
                nameof(options));
        }

        if (options.TimeSignatureNumerator.HasValue != options.TimeSignatureDenominator.HasValue)
        {
            throw new ArgumentException(
                "A time signature has two numbers: set both or neither.", nameof(options));
        }

        if (options.TimeSignatureNumerator.HasValue)
        {
            int numerator = options.TimeSignatureNumerator.Value;
            int denominator = options.TimeSignatureDenominator.Value;
            if (numerator < 1 || numerator > 16)
            {
                throw new ArgumentException(
                    "This model writes a time signature whose upper number is 1 to 16.", nameof(options));
            }

            if (denominator != 2 && denominator != 4 && denominator != 8 && denominator != 16)
            {
                throw new ArgumentException(
                    "This model writes a time signature whose lower number is 2, 4, 8 or 16.",
                    nameof(options));
            }
        }

        if (options.KeySignatureSharpsOrFlats.HasValue
            && (options.KeySignatureSharpsOrFlats.Value < -7 || options.KeySignatureSharpsOrFlats.Value > 7))
        {
            throw new ArgumentException(
                "A key signature has -7 to 7 sharps or flats.", nameof(options));
        }
    }

    private static bool DescribesAPiece(MidiGenerationOptions options) =>
        (options.Instruments != null && options.Instruments.Count > 0)
        || options.DrumKit.HasValue
        || options.BeatsPerMinute.HasValue
        || options.TimeSignatureNumerator.HasValue
        || options.TimeSignatureDenominator.HasValue
        || options.KeySignatureSharpsOrFlats.HasValue;

    private static void SevenBit(int value, string what)
    {
        if (value < 0 || value > 127)
        {
            throw new ArgumentException(
                what + " is a General MIDI program number, 0 to 127, and "
                + value.ToString(CultureInfo.InvariantCulture) + " is not one.",
                "options");
        }
    }
}

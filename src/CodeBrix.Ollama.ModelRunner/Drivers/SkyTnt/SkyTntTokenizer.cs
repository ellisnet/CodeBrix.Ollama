using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner; //was previously: midi_tokenizer.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// The model's own MIDI tokenizer, version two: the vocabulary it generates in, and the two directions between
/// a piece of music and rows of tokens.
/// </summary>
/// <remarks>
/// <para>
/// AN EVENT IS A FIXED ROW OF TOKENS. The first token says which of the six kinds of event it is, the next
/// ones are its parameters in the kind's own order, and the rest of the row is padding. The first three
/// parameters are always the same three: the beat (as a DISTANCE from the last event's beat), the position
/// inside that beat in sixteenths, and the track.
/// </para>
/// <para>
/// THE TOKENS ARE ALLOTTED IN ONE FIXED ORDER, and that order is the vocabulary: the padding, beginning and
/// ending tokens; then one token for each kind of event; then one contiguous block for each parameter family.
/// A bundle's <c>config.json</c> states the same facts without the order - a JSON object has none - so what
/// happens at load is that this layout is built and then CHECKED against the file, number for number and name
/// for name.
/// </para>
/// <para>
/// A PIECE IS READ WITH ITS OWN TIDYING UP, which the bundle asks for through <c>optimise_midi</c>: channels
/// and tracks are re-numbered into the order they are first used, an instrument is given to any channel that
/// never chose one, channels that carry no notes are dropped, and everything that happens before the first
/// note is gathered into a setup at the front. That is how the model was trained to see music, so a prompt
/// read any other way would not look like anything it has seen.
/// </para>
/// </remarks>
internal sealed partial class SkyTntTokenizer
{
    /// <summary>The only tokenizer version this driver implements.</summary>
    internal const string SupportedVersion = "v2";

    /// <summary>How many ticks a quarter note lasts in a piece this tokenizer writes out.</summary>
    internal const int TicksPerQuarterNote = 480;

    /// <summary>How many parts a beat is divided into: a position inside a beat is one of these.</summary>
    internal const int StepsPerBeat = 16;

    /// <summary>The name of the parameter every event begins with: the distance in beats from the last one.</summary>
    internal const string TimeParameter = "time1";

    /// <summary>The name of the parameter that says where inside the beat an event falls.</summary>
    internal const string WithinBeatParameter = "time2";

    /// <summary>The name of the parameter that says which track an event belongs to.</summary>
    internal const string TrackParameter = "track";

    /// <summary>The name of the parameter that says which channel an event is on.</summary>
    internal const string ChannelParameter = "channel";

    private static readonly string[] ParameterOrder =
    {
        "time1", "time2", "duration", "track", "channel", "pitch", "velocity", "patch", "controller", "value",
        "bpm", "nn", "dd", "sf", "mi",
    };

    private static readonly int[] ParameterSizes =
    {
        128, 16, 2048, 128, 16, 128, 128, 128, 128, 128, 384, 16, 4, 15, 2,
    };

    private static readonly string[] EventOrder =
    {
        "note", "patch_change", "control_change", "set_tempo", "time_signature", "key_signature",
    };

    private static readonly string[][] EventParameterNames =
    {
        new[] { "time1", "time2", "track", "channel", "pitch", "velocity", "duration" },
        new[] { "time1", "time2", "track", "channel", "patch" },
        new[] { "time1", "time2", "track", "channel", "controller", "value" },
        new[] { "time1", "time2", "track", "bpm" },
        new[] { "time1", "time2", "track", "nn", "dd" },
        new[] { "time1", "time2", "track", "sf", "mi" },
    };

    private readonly Dictionary<string, SkyTntEventType> _typesByName =
        new Dictionary<string, SkyTntEventType>(StringComparer.Ordinal);

    private readonly Dictionary<int, SkyTntEventType> _typesById = new Dictionary<int, SkyTntEventType>();

    private readonly Dictionary<string, SkyTntParameter> _parameters =
        new Dictionary<string, SkyTntParameter>(StringComparer.Ordinal);

    private readonly List<SkyTntEventType> _types = new List<SkyTntEventType>();

    private SkyTntTokenizer(bool optimiseMidi)
    {
        OptimiseMidi = optimiseMidi;

        //The order below IS the vocabulary. Three tokens of their own, then one for each kind of event, then a
        //contiguous block for each parameter family.
        int next = 0;
        PadId = next++;
        BeginningId = next++;
        EndId = next++;

        int[] eventIds = new int[EventOrder.Length];
        for (int i = 0; i < EventOrder.Length; i++) eventIds[i] = next++;

        for (int i = 0; i < ParameterOrder.Length; i++)
        {
            _parameters[ParameterOrder[i]] = new SkyTntParameter(ParameterOrder[i], next, ParameterSizes[i]);
            next += ParameterSizes[i];
        }

        VocabularySize = next;

        int longest = 0;
        for (int i = 0; i < EventOrder.Length; i++)
        {
            string[] names = EventParameterNames[i];
            SkyTntParameter[] parameters = new SkyTntParameter[names.Length];
            for (int j = 0; j < names.Length; j++) parameters[j] = _parameters[names[j]];

            SkyTntEventType type = new SkyTntEventType(EventOrder[i], eventIds[i], parameters);
            _types.Add(type);
            _typesByName[type.Name] = type;
            _typesById[type.Id] = type;
            if (names.Length > longest) longest = names.Length;
        }

        MaximumTokensPerEvent = longest + 1;
    }

    /// <summary>Whether a piece read in is tidied up the way the model was trained to see it.</summary>
    internal bool OptimiseMidi { get; }

    /// <summary>How many tokens there are altogether.</summary>
    internal int VocabularySize { get; }

    /// <summary>How long an event's row of tokens is.</summary>
    internal int MaximumTokensPerEvent { get; }

    /// <summary>The token that fills the unused end of a row.</summary>
    internal int PadId { get; }

    /// <summary>The token a piece starts with.</summary>
    internal int BeginningId { get; }

    /// <summary>The token that ends a piece.</summary>
    internal int EndId { get; }

    /// <summary>Every kind of event, in the order their tokens were allotted.</summary>
    internal IReadOnlyList<SkyTntEventType> EventTypes => _types;

    /// <summary>
    /// Builds the tokenizer a bundle asks for and checks it against what the bundle says, so that a
    /// configuration this driver does not implement is refused at load rather than found out later.
    /// </summary>
    /// <param name="configuration">What the bundle's <c>config.json</c> says.</param>
    /// <returns>The tokenizer.</returns>
    /// <exception cref="ModelLoadException">
    /// The bundle asks for another version, or its numbers and names do not match the layout implemented here.
    /// </exception>
    internal static SkyTntTokenizer FromConfiguration(SkyTntTokenizerConfiguration configuration)
    {
        if (!string.Equals(configuration.Version, SupportedVersion, StringComparison.Ordinal))
        {
            throw new ModelLoadException(
                "This bundle asks for MIDI tokenizer version '" + configuration.Version + "' and this driver"
                + " implements version '" + SupportedVersion + "' only.");
        }

        SkyTntTokenizer tokenizer = new SkyTntTokenizer(configuration.OptimiseMidi);

        Require(
            tokenizer.VocabularySize == configuration.VocabularySize,
            "vocabulary size", tokenizer.VocabularySize, configuration.VocabularySize);
        Require(
            tokenizer.MaximumTokensPerEvent == configuration.MaximumTokensPerEvent,
            "row length", tokenizer.MaximumTokensPerEvent, configuration.MaximumTokensPerEvent);
        Require(tokenizer.PadId == configuration.PadId, "padding token", tokenizer.PadId, configuration.PadId);
        Require(
            tokenizer.BeginningId == configuration.BeginningId,
            "beginning token", tokenizer.BeginningId, configuration.BeginningId);
        Require(tokenizer.EndId == configuration.EndId, "ending token", tokenizer.EndId, configuration.EndId);

        if (configuration.Events.Count != tokenizer._types.Count)
        {
            throw new ModelLoadException(
                "This bundle declares " + configuration.Events.Count.ToString(CultureInfo.InvariantCulture)
                + " kinds of event and this driver implements "
                + tokenizer._types.Count.ToString(CultureInfo.InvariantCulture) + ".");
        }

        foreach (SkyTntEventType type in tokenizer._types)
        {
            if (!configuration.Events.TryGetValue(type.Name, out IReadOnlyList<string> names))
            {
                throw new ModelLoadException(
                    "This bundle does not declare the '" + type.Name + "' event, which this driver expects.");
            }

            if (names.Count != type.Parameters.Count)
            {
                throw new ModelLoadException(
                    "This bundle's '" + type.Name + "' event has "
                    + names.Count.ToString(CultureInfo.InvariantCulture) + " parameters and this driver"
                    + " expects " + type.Parameters.Count.ToString(CultureInfo.InvariantCulture) + ".");
            }

            for (int i = 0; i < names.Count; i++)
            {
                if (!string.Equals(names[i], type.Parameters[i].Name, StringComparison.Ordinal))
                {
                    throw new ModelLoadException(
                        "This bundle's '" + type.Name + "' event has '" + names[i] + "' where this driver"
                        + " expects '" + type.Parameters[i].Name + "'.");
                }
            }
        }

        if (configuration.EventParameters.Count != tokenizer._parameters.Count)
        {
            throw new ModelLoadException(
                "This bundle declares "
                + configuration.EventParameters.Count.ToString(CultureInfo.InvariantCulture)
                + " parameter families and this driver implements "
                + tokenizer._parameters.Count.ToString(CultureInfo.InvariantCulture) + ".");
        }

        foreach (KeyValuePair<string, SkyTntParameter> parameter in tokenizer._parameters)
        {
            if (!configuration.EventParameters.TryGetValue(parameter.Key, out int size))
            {
                throw new ModelLoadException(
                    "This bundle does not declare the '" + parameter.Key + "' parameter, which this driver"
                    + " expects.");
            }

            Require(size == parameter.Value.Size, "size of '" + parameter.Key + "'", parameter.Value.Size, size);
        }

        return tokenizer;
    }

    /// <summary>The kind of event a token introduces, or <see langword="null"/> when it introduces none.</summary>
    /// <param name="token">The token.</param>
    /// <returns>The kind, or <see langword="null"/>.</returns>
    internal SkyTntEventType TypeForToken(int token) =>
        _typesById.TryGetValue(token, out SkyTntEventType type) ? type : null;

    /// <summary>The kind of event with a name, or <see langword="null"/> when there is none.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The kind, or <see langword="null"/>.</returns>
    internal SkyTntEventType TypeForName(string name) =>
        _typesByName.TryGetValue(name, out SkyTntEventType type) ? type : null;

    /// <summary>The parameter family with a name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The family.</returns>
    internal SkyTntParameter Parameter(string name) => _parameters[name];

    /// <summary>
    /// Turns an event into its row of tokens, padded out to the full length.
    /// </summary>
    /// <param name="row">The event.</param>
    /// <returns>
    /// The row, or <see langword="null"/> when a parameter is outside what its family can express - which is
    /// how the upstream tokenizer drops an event it cannot write rather than writing a wrong one.
    /// </returns>
    internal int[] EventToTokens(SkyTntEventRow row)
    {
        SkyTntEventType type = row.Type;
        for (int i = 0; i < type.Parameters.Count; i++)
        {
            if (!type.Parameters[i].Holds(row.Values[i])) return null;
        }

        int[] tokens = new int[MaximumTokensPerEvent];
        tokens[0] = type.Id;
        for (int i = 0; i < type.Parameters.Count; i++)
        {
            tokens[i + 1] = type.Parameters[i].TokenFor(row.Values[i]);
        }

        for (int i = type.Parameters.Count + 1; i < tokens.Length; i++) tokens[i] = PadId;
        return tokens;
    }

    /// <summary>
    /// Turns a row of tokens back into an event.
    /// </summary>
    /// <param name="tokens">The row, which may be longer than the event needs.</param>
    /// <returns>
    /// The event, or <see langword="null"/> when the row does not introduce one, is too short, or holds a
    /// token outside the family the position calls for.
    /// </returns>
    internal SkyTntEventRow TokensToEvent(IReadOnlyList<int> tokens)
    {
        if (tokens == null || tokens.Count == 0) return null;

        SkyTntEventType type = TypeForToken(tokens[0]);
        if (type == null) return null;
        if (tokens.Count <= type.Parameters.Count) return null;

        int[] values = new int[type.Parameters.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = tokens[i + 1] - type.Parameters[i].FirstId;
            if (!type.Parameters[i].Holds(values[i])) return null;
        }

        return new SkyTntEventRow(type, values);
    }

    /// <summary>The row of tokens a piece starts with: the beginning token, then padding.</summary>
    /// <returns>The row.</returns>
    internal int[] BeginningRow() => SingleTokenRow(BeginningId);

    /// <summary>The row of tokens a piece ends with: the ending token, then padding.</summary>
    /// <returns>The row.</returns>
    internal int[] EndRow() => SingleTokenRow(EndId);

    private int[] SingleTokenRow(int token)
    {
        int[] row = new int[MaximumTokensPerEvent];
        row[0] = token;
        for (int i = 1; i < row.Length; i++) row[i] = PadId;
        return row;
    }

    private static void Require(bool held, string what, int expected, int stated)
    {
        if (held) return;
        throw new ModelLoadException(
            "This bundle states a " + what + " of " + stated.ToString(CultureInfo.InvariantCulture)
            + " and the MIDI tokenizer this driver implements has "
            + expected.ToString(CultureInfo.InvariantCulture) + ".");
    }
}

using System;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: midi_tokenizer.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// One event as the tokenizer works with it: its kind, and its parameters as plain whole numbers in the order
/// the kind declares them - the first three always being the beat, the position inside the beat, and the
/// track.
/// </summary>
/// <remarks>
/// It is MUTABLE on purpose. The upstream tokenizer edits these rows in place while it is working - it
/// shortens a note that the next one of the same pitch cuts off, it re-numbers channels and tracks, it turns
/// an absolute beat into a distance from the last one - and a row that has been put in a collection is edited
/// through the reference the collection holds. A port that copied instead would have to find every place that
/// relies on that and would quietly differ where it missed one.
/// </remarks>
internal sealed class SkyTntEventRow
{
    /// <summary>Creates a row.</summary>
    /// <param name="type">Which kind of event it is.</param>
    /// <param name="values">Its parameters, in the kind's own order. The array is taken, not copied.</param>
    internal SkyTntEventRow(SkyTntEventType type, int[] values)
    {
        Type = type;
        Values = values;
    }

    /// <summary>Which kind of event it is.</summary>
    internal SkyTntEventType Type { get; }

    /// <summary>Its parameters, in the kind's own order.</summary>
    internal int[] Values { get; }

    /// <summary>The beat it falls on, counted as a whole number of quarter notes.</summary>
    internal int Beat
    {
        get => Values[0];
        set => Values[0] = value;
    }

    /// <summary>Where inside that beat it falls, in sixteenths of a beat.</summary>
    internal int WithinBeat => Values[1];

    /// <summary>Which track it belongs to.</summary>
    internal int Track
    {
        get => Values[2];
        set => Values[2] = value;
    }

    /// <summary>An independent copy, which the upstream tokenizer takes in two places.</summary>
    /// <returns>The copy.</returns>
    internal SkyTntEventRow Copy() => new SkyTntEventRow(Type, (int[])Values.Clone());

    /// <summary>
    /// The identity the upstream tokenizer groups rows by: the kind, the position, the track, and as many of
    /// the remaining parameters as the kind uses for the purpose.
    /// </summary>
    /// <param name="trailingToDrop">How many parameters at the end to leave out.</param>
    /// <returns>A key that two rows share exactly when the upstream dictionary would collide them.</returns>
    internal string KeyWithout(int trailingToDrop) => Key(0, Values.Length - trailingToDrop);

    /// <summary>
    /// The identity the upstream tokenizer's SETUP pass groups rows by, which leaves the position out and
    /// starts at the track.
    /// </summary>
    /// <param name="trailingToDrop">How many parameters at the end to leave out.</param>
    /// <returns>The key.</returns>
    internal string KeyFromTrackWithout(int trailingToDrop) => Key(2, Values.Length - trailingToDrop);

    /// <summary>Describes the row in one line, for diagnostics.</summary>
    /// <returns>The description.</returns>
    public override string ToString() => Type.Name + " " + Key(0, Values.Length);

    private string Key(int from, int to)
    {
        StringBuilder text = new StringBuilder(Type.Name);
        for (int i = from; i < to; i++)
        {
            text.Append('|');
            text.Append(Values[i].ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// One event the publisher's own generation loop produced: the row of tokens it sampled, and what that row
/// decodes to.
/// </summary>
public sealed class SkyTntOracleEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="tokens">The row of tokens, padding and all.</param>
    /// <param name="name">The kind of event, or <see langword="null"/> when the row decodes to none.</param>
    /// <param name="values">Its parameters, in the kind's own order.</param>
    public SkyTntOracleEvent(IReadOnlyList<int> tokens, string name, IReadOnlyList<int> values)
    {
        Tokens = tokens;
        Name = name;
        Values = values;
    }

    /// <summary>The row of tokens, padding and all.</summary>
    public IReadOnlyList<int> Tokens { get; }

    /// <summary>The kind of event, or <see langword="null"/> when the row decodes to none.</summary>
    public string Name { get; }

    /// <summary>Its parameters, in the kind's own order.</summary>
    public IReadOnlyList<int> Values { get; }

    /// <summary>
    /// The event written out as one line, in the form the managed events are written out in - which is what
    /// the two are compared as.
    /// </summary>
    /// <remarks>
    /// A tempo is written as the MICROSECONDS a file holds rather than the beats a minute the model wrote,
    /// because that is the one parameter the two sides do not hold in the same unit and the conversion has
    /// exactly one direction that is exact.
    /// </remarks>
    /// <returns>The line.</returns>
    public string Line()
    {
        StringBuilder text = new StringBuilder(Name);
        for (int i = 0; i < Values.Count; i++)
        {
            text.Append(' ');
            if (Name == "set_tempo" && i == 3)
            {
                text.Append(Microseconds(Values[i]).ToString(CultureInfo.InvariantCulture));
                continue;
            }

            text.Append(Values[i].ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>The microseconds a quarter note lasts at a whole number of beats a minute.</summary>
    /// <param name="beatsPerMinute">The speed; nought is read as one, as the publisher's code does.</param>
    /// <returns>The microseconds.</returns>
    public static long Microseconds(int beatsPerMinute) =>
        (long)((60.0 / (beatsPerMinute == 0 ? 1 : beatsPerMinute)) * 1000000.0);
}

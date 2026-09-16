using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Watches the text of a generation for any of the request's stop sequences, holding back the tail that
/// could still turn out to be the start of one.
/// </summary>
/// <remarks>
/// <para>
/// A stop sequence is a string, and the model produces tokens, so a stop sequence is quite ordinarily split
/// across two or three of them. Emitting each token as it arrives would let the first half of a stop
/// sequence reach the caller before the second half proved what it was. The detector therefore never emits a
/// suffix that is a prefix of some stop sequence: that suffix is held until the next token either completes
/// the stop sequence - in which case everything up to it is emitted and generation ends - or rules it out,
/// in which case the held text is emitted with the rest.
/// </para>
/// <para>
/// When two stop sequences match, the one that matches EARLIEST wins, and between two that start at the same
/// place, the longer. Neither the stop sequence nor anything after it is ever emitted.
/// </para>
/// </remarks>
internal sealed class StopSequenceDetector
{
    private readonly string[] stops;
    private readonly StringBuilder held = new StringBuilder();

    /// <summary>Creates a detector for a set of stop sequences.</summary>
    /// <param name="stopSequences">The stop sequences; null and empty entries are ignored. May be null.</param>
    public StopSequenceDetector(IEnumerable<string> stopSequences)
    {
        List<string> wanted = new List<string>();
        if (stopSequences != null)
        {
            foreach (string stop in stopSequences)
            {
                if (!string.IsNullOrEmpty(stop)) wanted.Add(stop);
            }
        }

        stops = wanted.ToArray();
    }

    /// <summary>Whether there is anything to watch for at all.</summary>
    public bool IsWatching => stops.Length > 0;

    /// <summary>Whether a stop sequence has been seen.</summary>
    public bool IsStopped { get; private set; }

    /// <summary>The stop sequence that matched, or <see langword="null"/> when none has.</summary>
    public string MatchedStopSequence { get; private set; }

    /// <summary>Adds text and returns the part of it that can be emitted now.</summary>
    /// <param name="text">The text produced since the last call.</param>
    /// <returns>The text to emit, which may be empty.</returns>
    public string Append(string text)
    {
        if (IsStopped) return string.Empty;
        if (!IsWatching) return text ?? string.Empty;
        if (string.IsNullOrEmpty(text) && held.Length == 0) return string.Empty;

        if (!string.IsNullOrEmpty(text)) held.Append(text);
        string buffer = held.ToString();

        int matchAt = -1;
        string matched = null;

        foreach (string stop in stops)
        {
            int index = buffer.IndexOf(stop, StringComparison.Ordinal);
            if (index < 0) continue;

            if (matchAt < 0 || index < matchAt || (index == matchAt && stop.Length > matched.Length))
            {
                matchAt = index;
                matched = stop;
            }
        }

        if (matchAt >= 0)
        {
            IsStopped = true;
            MatchedStopSequence = matched;
            held.Clear();
            return buffer.Substring(0, matchAt);
        }

        int hold = LongestPartialSuffix(buffer);
        held.Clear();
        if (hold > 0) held.Append(buffer, buffer.Length - hold, hold);

        return hold == buffer.Length ? string.Empty : buffer.Substring(0, buffer.Length - hold);
    }

    /// <summary>
    /// Ends the watch and returns whatever was held back. Nothing is returned once a stop sequence has
    /// matched: the held text was the stop sequence itself.
    /// </summary>
    /// <returns>The text, usually empty.</returns>
    public string Flush()
    {
        if (IsStopped || held.Length == 0) return string.Empty;

        string remainder = held.ToString();
        held.Clear();
        return remainder;
    }

    private int LongestPartialSuffix(string buffer)
    {
        int longest = 0;

        foreach (string stop in stops)
        {
            int most = Math.Min(stop.Length - 1, buffer.Length);
            for (int length = most; length > longest; length--)
            {
                if (string.CompareOrdinal(buffer, buffer.Length - length, stop, 0, length) == 0)
                {
                    longest = length;
                    break;
                }
            }
        }

        return longest;
    }
}

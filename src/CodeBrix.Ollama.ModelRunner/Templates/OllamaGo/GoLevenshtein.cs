using System;

namespace CodeBrix.Ollama.ModelRunner; //was previously: agnivade/levenshtein levenshtein.go (MIT);

/// <summary>
/// The edit distance Ollama uses to match an arbitrary chat template to one of its built-ins. It counts
/// code points, not UTF-16 units, so it agrees with the Go implementation on non-ASCII templates.
/// </summary>
internal static class GoLevenshtein
{
    /// <summary>
    /// Computes the Levenshtein distance between two strings.
    /// </summary>
    /// <param name="left">The first string. <see langword="null"/> counts as empty.</param>
    /// <param name="right">The second string. <see langword="null"/> counts as empty.</param>
    /// <returns>The number of single-character edits that turn one string into the other.</returns>
    internal static int ComputeDistance(string left, string right)
    {
        int[] a = ToCodePoints(left);
        int[] b = ToCodePoints(right);
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        if (a.Length > b.Length)
        {
            int[] swap = a;
            a = b;
            b = swap;
        }

        int[] previous = new int[a.Length + 1];
        int[] current = new int[a.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            previous[i] = i;
        }

        for (int j = 1; j <= b.Length; j++)
        {
            current[0] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[i] = Math.Min(Math.Min(current[i - 1] + 1, previous[i] + 1), previous[i - 1] + cost);
            }

            int[] swap = previous;
            previous = current;
            current = swap;
        }

        return previous[a.Length];
    }

    private static int[] ToCodePoints(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Array.Empty<int>();
        }

        int[] buffer = new int[value.Length];
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                buffer[count++] = char.ConvertToUtf32(value[i], value[i + 1]);
                i++;
                continue;
            }

            buffer[count++] = value[i];
        }

        if (count == buffer.Length)
        {
            return buffer;
        }

        int[] trimmed = new int[count];
        Array.Copy(buffer, trimmed, count);
        return trimmed;
    }
}

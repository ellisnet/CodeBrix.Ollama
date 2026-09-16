using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Reads the reference logits the native conformance gate records, so a managed decode can be held to
/// exactly the same numbers the C gate holds every native build to.
/// </summary>
/// <remarks>
/// The format is one line per prompt position,
/// <c>pos &lt;i&gt; token &lt;id&gt; argmax &lt;id&gt; logits &lt;v0&gt; ... &lt;vN&gt;</c>, with '#' comment
/// lines. The file is written by llama-native-tools and is the same one
/// <see cref="NativeConformanceTests"/> reads; this reader exists because that one's is private to it.
/// </remarks>
public static class EngineExpectedLogits
{
    /// <summary>The twelve token ids the conformance gate feeds the tiny model.</summary>
    public static IReadOnlyList<int> Prompt { get; } = new[] { 3, 17, 42, 8, 8, 61, 0, 25, 33, 12, 50, 7 };

    /// <summary>Reads the reference rows.</summary>
    /// <param name="path">The path of EXPECTED.txt.</param>
    /// <returns>One row per prompt position, in order.</returns>
    /// <exception cref="FormatException">A line is not in the expected shape.</exception>
    public static IReadOnlyList<EngineExpectedLogitRow> Read(string path)
    {
        List<EngineExpectedLogitRow> rows = new List<EngineExpectedLogitRow>();

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 8 || parts[0] != "pos" || parts[2] != "token" || parts[4] != "argmax"
                || parts[6] != "logits")
            {
                throw new FormatException($"'{path}' has a line this reader does not understand: {line}");
            }

            double[] logits = new double[parts.Length - 7];
            for (int i = 0; i < logits.Length; i++)
            {
                logits[i] = double.Parse(parts[i + 7], CultureInfo.InvariantCulture);
            }

            rows.Add(new EngineExpectedLogitRow
            {
                Position = int.Parse(parts[1], CultureInfo.InvariantCulture),
                Token = int.Parse(parts[3], CultureInfo.InvariantCulture),
                Argmax = int.Parse(parts[5], CultureInfo.InvariantCulture),
                Logits = logits,
            });
        }

        return rows;
    }
}

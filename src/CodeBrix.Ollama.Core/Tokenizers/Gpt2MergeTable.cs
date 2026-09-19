using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.Core; //was previously: gguf-py/gguf/vocab.py@b10221;

/// <summary>
/// The merge table of a GPT-2 byte-level BPE tokenizer, read out of the lines of a <c>merges.txt</c>.
/// </summary>
/// <remarks>
/// This is a PRIMITIVE and deliberately knows nothing beyond the file's shape: two pieces to a line, an
/// optional <c>#version</c> header on the first line only, and a malformed line ignored rather than
/// refused. Both sides of this repository need the same reading of that file - the checkpoint conversion,
/// which writes the table into a GGUF vocabulary, and a tokenizer that has to re-derive the merges to
/// encode text - so the rule lives once, here, rather than once on each side.
/// </remarks>
internal static class Gpt2MergeTable
{
    /// <summary>Reads a merge table out of the lines of a <c>merges.txt</c>.</summary>
    /// <param name="lines">The file's lines.</param>
    /// <returns>The merges, in rank order, each one the two pieces separated by a single space.</returns>
    internal static List<string> Parse(IReadOnlyList<string> lines)
    {
        var merges = new List<string>();
        int start = 0;
        if (lines.Count > 0 && lines[0].Trim().StartsWith("#", StringComparison.Ordinal))
        {
            start = 1;
        }

        for (int i = start; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split((char[])null, 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                // The engine warns and ignores a malformed line rather than refusing the checkpoint.
                continue;
            }

            merges.Add(parts[0] + " " + parts[1]);
        }

        return merges;
    }
}

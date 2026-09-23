using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Experimental: prepares recent complete bars as a self-contained musical prompt.</summary>
internal static class Remigen2ContextWindow
{
    internal static int[] Take(IReadOnlyList<int> previous, IReadOnlyList<int> generated,
        IReadOnlyList<string> vocabulary, IReadOnlyDictionary<string, int> tokenIds, int bars)
    {
        var words = previous.Concat(generated).Select(id => vocabulary[id]).ToList();
        if (words.Count != 0 && words[0] is "Q1" or "Q2" or "Q3" or "Q4" or "None") words.RemoveAt(0);
        Remigen2Decoder.CleanEnding(words);
        if (words.Count != 0 && words[words.Count - 1] != "b-1") words.Add("b-1");
        if (words.Count == 0) return Array.Empty<int>();

        var starts = new List<int> { 0 };
        for (int i = 0; i < words.Count - 1; i++)
            if (words[i] == "b-1") starts.Add(i + 1);
        int start = starts[Math.Max(0, starts.Count - bars)];
        string signature = null, tempo = null, instrument = "i-0";
        for (int i = 0; i < start; i++)
        {
            if (words[i][0] == 's') signature = words[i];
            else if (words[i][0] == 't') tempo = words[i];
            else if (words[i][0] == 'i') instrument = words[i];
        }
        var tail = words.Skip(start).ToList();
        // The retained first bar may have inherited metadata/instrument from the discarded bars.
        // Restore that context in the prompt only; it is never emitted again as music.
        if (signature != null && tail[0][0] != 's') tail.Insert(0, signature);
        int headerEnd = tail.FindIndex(w => w[0] is 'o' or 'b');
        if (tempo != null && !tail.Take(headerEnd < 0 ? tail.Count : headerEnd).Any(w => w[0] == 't'))
            tail.Insert(tail[0][0] == 's' ? 1 : 0, tempo);
        int firstPitch = tail.FindIndex(w => w[0] == 'p');
        if (firstPitch >= 0 && !tail.Take(firstPitch).Any(w => w[0] == 'i')) tail.Insert(firstPitch, instrument);
        return tail.Select(w => tokenIds[w]).ToArray();
    }
}

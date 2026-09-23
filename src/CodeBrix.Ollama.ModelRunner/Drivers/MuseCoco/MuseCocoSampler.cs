using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

internal static class MuseCocoSampler
{
    internal static int Sample(float[] logits, int eos, bool allowEos, Remigen2Grammar grammar,
        MuseCocoGenerationOptions options, GenerationRandom random)
    {
        var order = new List<int>(logits.Length);
        for (int i = 0; i < logits.Length; i++)
        {
            if ((i == eos && !allowEos) || !grammar.Allows(i)) continue;
            if (!float.IsFinite(logits[i])) throw new InferenceException("MuseCoco returned a non-finite logit.");
            order.Add(i);
        }
        if (order.Count == 0) throw new InferenceException("No music token is available for sampling.");
        order.Sort((left, right) =>
        {
            int byLogit = logits[right].CompareTo(logits[left]);
            return byLogit == 0 ? left.CompareTo(right) : byLogit;
        });
        if (options.TopK == 1) return order[0];
        int count = Math.Min(options.TopK, order.Count);
        var probabilities = new double[count];
        double total = 0;
        for (int i = 0; i < count; i++)
        {
            probabilities[i] = Math.Exp(((double)logits[order[i]] - logits[order[0]]) / options.Temperature);
            total += probabilities[i];
        }
        double cumulative = 0;
        int keep = 0;
        do
        {
            cumulative += probabilities[keep++];
        } while (keep < count && cumulative < options.TopP * total);
        double draw = random.NextDouble() * cumulative;
        cumulative = 0;
        for (int i = 0; i < keep; i++)
        {
            cumulative += probabilities[i];
            if (draw < cumulative) return order[i];
        }
        return order[keep - 1];
    }
}

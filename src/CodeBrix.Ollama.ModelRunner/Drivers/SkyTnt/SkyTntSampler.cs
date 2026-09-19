using System;

namespace CodeBrix.Ollama.ModelRunner; //was previously: app_onnx.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// How one token is chosen out of the model's answer: the publisher's own temperature, nucleus and top-k
/// sampling, with the DISALLOWED TOKENS taken out first.
/// </summary>
/// <remarks>
/// <para>
/// THE MASK IS APPLIED AFTER THE SOFTMAX, not before it, and that is the upstream order rather than a
/// simplification: the probabilities are worked out over the WHOLE vocabulary and the ones that are not
/// allowed at this position in the event's row are then set to nought. It gives the same choice as masking
/// beforehand would, and it is what the nucleus threshold is measured against - so a port that masked first
/// would take a different slice of the distribution.
/// </para>
/// <para>
/// NUCLEUS SAMPLING here keeps every token whose probability, together with everything MORE likely than it,
/// has not yet passed the threshold - the sum BEFORE it is added, which is what lets the threshold keep at
/// least one token however sharp the distribution is. Top-k then keeps at most that many of them, and what is
/// left is made to add up to one again and drawn from.
/// </para>
/// <para>
/// WITH A TOP-K OF ONE THERE IS NO RANDOMNESS AT ALL: the most likely allowed token is taken, ties going to
/// the lower token, and no number is drawn from the generator. That is the setting the port is proved against
/// the publisher's own Python on.
/// </para>
/// </remarks>
internal static class SkyTntSampler
{
    /// <summary>Chooses one token.</summary>
    /// <param name="logits">The model's answer, one number per token.</param>
    /// <param name="allowed">Which tokens this position of an event's row may hold; the rest are refused.</param>
    /// <param name="temperature">
    /// How much the answer is flattened before it is read as probabilities. One leaves it as it is; below one
    /// sharpens it; above one flattens it.
    /// </param>
    /// <param name="topP">Keep the most likely tokens up to this much probability; one keeps them all.</param>
    /// <param name="topK">Keep at most this many; one makes the choice the most likely token.</param>
    /// <param name="random">Where the choice is drawn from, when there is a choice to make.</param>
    /// <returns>The token.</returns>
    internal static int Sample(
        float[] logits, bool[] allowed, double temperature, double topP, int topK, SkyTntRandom random)
    {
        int count = allowed.Length;

        //The most likely allowed token, which is the whole of the answer when only one is kept. It is worked
        //out from the logits themselves: the softmax does not change which is largest, and this way a
        //greedy generation costs no sorting.
        int best = -1;
        float bestLogit = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            if (!allowed[i]) continue;
            if (best < 0 || logits[i] > bestLogit)
            {
                best = i;
                bestLogit = logits[i];
            }
        }

        if (best < 0)
        {
            throw new InferenceException(
                "No token is allowed at this position of the event, so there is nothing to choose from.");
        }

        if (topK <= 1) return best;

        float[] probabilities = Softmax(logits, temperature, count);
        for (int i = 0; i < count; i++)
        {
            if (!allowed[i]) probabilities[i] = 0f;
        }

        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        Array.Sort(order, (left, right) =>
        {
            int byProbability = probabilities[right].CompareTo(probabilities[left]);
            return byProbability != 0 ? byProbability : left.CompareTo(right);
        });

        int keep = topK < count ? topK : count;
        double[] kept = new double[keep];
        double before = 0;
        double total = 0;
        for (int i = 0; i < keep; i++)
        {
            double probability = probabilities[order[i]];
            kept[i] = before > topP ? 0 : probability;
            before += probability;
            total += kept[i];
        }

        if (!(total > 0)) return best;

        double drawn = random.NextDouble() * total;
        double running = 0;
        for (int i = 0; i < keep; i++)
        {
            running += kept[i];
            if (drawn < running) return order[i];
        }

        //Only reachable when the running total falls a rounding error short of the number drawn.
        for (int i = keep - 1; i >= 0; i--)
        {
            if (kept[i] > 0) return order[i];
        }

        return best;
    }

    /// <summary>The model's answer read as probabilities, flattened by a temperature first.</summary>
    /// <param name="logits">The answer.</param>
    /// <param name="temperature">The flattening; one leaves the answer as it is.</param>
    /// <param name="count">How many of the numbers to read.</param>
    /// <returns>The probabilities, adding up to one.</returns>
    internal static float[] Softmax(float[] logits, double temperature, int count)
    {
        float[] values = new float[count];
        float scale = (float)(1.0 / temperature);
        float largest = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            float value = temperature == 1.0 ? logits[i] : logits[i] * scale;
            values[i] = value;
            if (value > largest) largest = value;
        }

        float total = 0;
        for (int i = 0; i < count; i++)
        {
            float value = MathF.Exp(values[i] - largest);
            values[i] = value;
            total += value;
        }

        if (total > 0)
        {
            for (int i = 0; i < count; i++) values[i] /= total;
        }

        return values;
    }
}

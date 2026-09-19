using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp src/llama-sampling.cpp (chain order and each stage's rule);

/// <summary>
/// Turns one row of the model's answer into one token: the penalties, the truncations, the temperature and
/// the draw, in the order the native half of this library applies them.
/// </summary>
/// <remarks>
/// <para>
/// ORDER IS NOT A DETAIL. Penalties act on raw scores and so come first; the truncations run from the
/// coarsest to the finest; temperature is applied last, immediately before the draw, because every earlier
/// stage compares probabilities and temperature changes them. This is the order
/// <see cref="EngineSamplerChain"/> builds for the native engine, written out here in managed code so that
/// the same <see cref="SamplingOptions"/> mean the same thing on both routes.
/// </para>
/// <para>
/// A TEMPERATURE OF NOUGHT IS GREEDY, and greedy is the one setting where the two routes - and this library
/// and another engine running the same graph - are claimed to produce the SAME tokens: the largest score
/// wins, ties go to the lower token number, and no random number is drawn at all. Every other setting draws
/// from this library's own stream of random numbers (<see cref="CausalLmRandom"/>), which is not the native
/// engine's, so a seed means one thing here and another thing there.
/// </para>
/// <para>
/// WHAT THE PENALTIES SEE is the tokens this request GENERATED, never the tokens of its prompt - the same
/// choice the native path makes, and stated here because it makes a repeat penalty act on a shorter history
/// than the same number would elsewhere.
/// </para>
/// </remarks>
internal sealed class CausalLmSampler
{
    private readonly SamplingOptions _options;
    private readonly CausalLmRandom _random;
    private readonly Dictionary<int, int> _counts = new Dictionary<int, int>();
    private readonly List<int> _generated = new List<int>();

    /// <summary>Creates the sampler for one request.</summary>
    /// <param name="options">The sampling parameters, already copied.</param>
    /// <param name="seed">The seed the draw starts from.</param>
    internal CausalLmSampler(SamplingOptions options, ulong seed)
    {
        _options = options;
        _random = new CausalLmRandom(seed);
    }

    /// <summary>Whether this request takes the most likely token every time.</summary>
    internal bool IsGreedy => _options.Temperature <= 0.0f;

    /// <summary>Remembers a token the request generated, which is what the penalties act on.</summary>
    /// <param name="token">The token.</param>
    internal void Accept(int token)
    {
        _generated.Add(token);
        _counts.TryGetValue(token, out int count);
        _counts[token] = count + 1;

        int window = Window();
        if (window <= 0 || _generated.Count <= window) return;

        //The window is the last N tokens, so the one that fell out of it stops counting.
        int dropped = _generated[_generated.Count - window - 1];
        if (!_counts.TryGetValue(dropped, out int droppedCount)) return;

        if (droppedCount <= 1) _counts.Remove(dropped);
        else _counts[dropped] = droppedCount - 1;
    }

    /// <summary>Chooses one token from a row of scores.</summary>
    /// <param name="logits">The scores, one per token number. It is modified in place.</param>
    /// <returns>The chosen token.</returns>
    /// <exception cref="InferenceException">The row holds no usable score.</exception>
    internal int Choose(float[] logits)
    {
        Penalise(logits);

        if (IsGreedy) return ArgMax(logits);

        List<CausalLmCandidate> candidates = Select(logits);
        Typical(candidates);
        TopP(candidates);
        MinP(candidates);

        return Draw(candidates);
    }

    /// <summary>The largest score's token, ties going to the lower token number.</summary>
    /// <param name="logits">The scores.</param>
    /// <returns>The token.</returns>
    internal static int ArgMax(float[] logits)
    {
        int best = -1;
        float highest = float.NegativeInfinity;
        for (int i = 0; i < logits.Length; i++)
        {
            float value = logits[i];
            if (float.IsNaN(value) || value <= highest) continue;

            highest = value;
            best = i;
        }

        if (best < 0)
        {
            throw new InferenceException(
                "The model's answer holds no usable score, so no token can be chosen from it.");
        }

        return best;
    }

    private int Window() => _options.RepeatLastN < 0 ? int.MaxValue : _options.RepeatLastN;

    private void Penalise(float[] logits)
    {
        bool penalising = _options.RepeatLastN != 0
            && (Math.Abs(_options.RepeatPenalty - 1.0f) > float.Epsilon
                || Math.Abs(_options.FrequencyPenalty) > float.Epsilon
                || Math.Abs(_options.PresencePenalty) > float.Epsilon);

        if (!penalising) return;

        foreach (KeyValuePair<int, int> seen in _counts)
        {
            int token = seen.Key;
            if (token < 0 || token >= logits.Length) continue;

            float logit = logits[token];

            //A score above nought is divided and one below is multiplied, so that the penalty always pushes
            //towards nought whichever side of it the score is on.
            logit = logit <= 0.0f ? logit * _options.RepeatPenalty : logit / _options.RepeatPenalty;
            logit -= seen.Value * _options.FrequencyPenalty + _options.PresencePenalty;
            logits[token] = logit;
        }
    }

    private List<CausalLmCandidate> Select(float[] logits)
    {
        int keep = _options.TopK > 0 && _options.TopK < logits.Length ? _options.TopK : logits.Length;

        List<CausalLmCandidate> candidates = new List<CausalLmCandidate>(keep);
        if (keep == logits.Length)
        {
            for (int i = 0; i < logits.Length; i++)
            {
                if (!float.IsNaN(logits[i])) candidates.Add(new CausalLmCandidate(i, logits[i]));
            }
        }
        else
        {
            //Only the best few are wanted, so the whole vocabulary is never sorted: the smallest of the
            //running best is kept at the front of a heap and everything below it is dropped as it is read.
            CausalLmCandidate[] heap = new CausalLmCandidate[keep];
            int held = 0;
            for (int i = 0; i < logits.Length; i++)
            {
                float value = logits[i];
                if (float.IsNaN(value)) continue;

                if (held < keep)
                {
                    heap[held] = new CausalLmCandidate(i, value);
                    held++;
                    if (held == keep) BuildHeap(heap, keep);
                    continue;
                }

                //Ties go to the LOWER token number, and the vocabulary is read in order, so a score that only
                //equals the weakest kept one is dropped.
                if (value <= heap[0].Logit) continue;

                heap[0] = new CausalLmCandidate(i, value);
                SiftDown(heap, keep, 0);
            }

            for (int i = 0; i < held; i++) candidates.Add(heap[i]);
        }

        candidates.Sort(CausalLmCandidate.Strongest);
        Softmax(candidates);
        return candidates;
    }

    private void Typical(List<CausalLmCandidate> candidates)
    {
        if (_options.TypicalP >= 1.0f || candidates.Count <= 1) return;

        double entropy = 0.0;
        foreach (CausalLmCandidate candidate in candidates)
        {
            if (candidate.Probability > 0.0) entropy -= candidate.Probability * Math.Log(candidate.Probability);
        }

        //How far each token's surprise is from the average surprise; the least surprising-by-that-measure are
        //kept, which is what "locally typical" means.
        List<CausalLmCandidate> ordered = new List<CausalLmCandidate>(candidates);
        ordered.Sort((left, right) => Shift(left, entropy).CompareTo(Shift(right, entropy)));

        double sum = 0.0;
        int keep = 0;
        foreach (CausalLmCandidate candidate in ordered)
        {
            sum += candidate.Probability;
            keep++;
            if (sum >= _options.TypicalP) break;
        }

        HashSet<int> wanted = new HashSet<int>();
        for (int i = 0; i < keep; i++) wanted.Add(ordered[i].Token);

        candidates.RemoveAll(candidate => !wanted.Contains(candidate.Token));
        Softmax(candidates);
    }

    private void TopP(List<CausalLmCandidate> candidates)
    {
        if (_options.TopP >= 1.0f || candidates.Count <= 1) return;

        double sum = 0.0;
        int keep = candidates.Count;
        for (int i = 0; i < candidates.Count; i++)
        {
            sum += candidates[i].Probability;
            if (sum >= _options.TopP)
            {
                keep = i + 1;
                break;
            }
        }

        if (keep < candidates.Count) candidates.RemoveRange(keep, candidates.Count - keep);
        Softmax(candidates);
    }

    private void MinP(List<CausalLmCandidate> candidates)
    {
        if (_options.MinP <= 0.0f || candidates.Count <= 1) return;

        //The list is strongest first, so the first entry is the most likely one and the threshold is a share
        //of it.
        double threshold = candidates[0].Probability * _options.MinP;
        int keep = candidates.Count;
        for (int i = 1; i < candidates.Count; i++)
        {
            if (candidates[i].Probability >= threshold) continue;

            keep = i;
            break;
        }

        if (keep < 1) keep = 1;
        if (keep < candidates.Count) candidates.RemoveRange(keep, candidates.Count - keep);
        Softmax(candidates);
    }

    private int Draw(List<CausalLmCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            throw new InferenceException(
                "Every token was ruled out by the sampling settings, so none can be chosen.");
        }

        if (candidates.Count == 1) return candidates[0].Token;

        //Temperature is applied here, immediately before the draw, because every stage above compares
        //probabilities and temperature changes them.
        for (int i = 0; i < candidates.Count; i++)
        {
            candidates[i] = candidates[i].WithLogit(candidates[i].Logit / _options.Temperature);
        }

        Softmax(candidates);

        double chosen = _random.NextDouble();
        double sum = 0.0;
        for (int i = 0; i < candidates.Count; i++)
        {
            sum += candidates[i].Probability;
            if (chosen < sum) return candidates[i].Token;
        }

        return candidates[candidates.Count - 1].Token;
    }

    private static double Shift(CausalLmCandidate candidate, double entropy)
    {
        double surprise = candidate.Probability > 0.0 ? -Math.Log(candidate.Probability) : double.MaxValue;
        return Math.Abs(surprise - entropy);
    }

    private static void Softmax(List<CausalLmCandidate> candidates)
    {
        if (candidates.Count == 0) return;

        float highest = float.NegativeInfinity;
        foreach (CausalLmCandidate candidate in candidates)
        {
            if (candidate.Logit > highest) highest = candidate.Logit;
        }

        double total = 0.0;
        for (int i = 0; i < candidates.Count; i++)
        {
            double weight = Math.Exp(candidates[i].Logit - highest);
            candidates[i] = candidates[i].WithProbability(weight);
            total += weight;
        }

        if (total <= 0.0) total = 1.0;
        for (int i = 0; i < candidates.Count; i++)
        {
            candidates[i] = candidates[i].WithProbability(candidates[i].Probability / total);
        }
    }

    private static void BuildHeap(CausalLmCandidate[] heap, int count)
    {
        for (int i = (count / 2) - 1; i >= 0; i--) SiftDown(heap, count, i);
    }

    private static void SiftDown(CausalLmCandidate[] heap, int count, int at)
    {
        while (true)
        {
            int smallest = at;
            int left = (2 * at) + 1;
            int right = left + 1;

            if (left < count && heap[left].Logit < heap[smallest].Logit) smallest = left;
            if (right < count && heap[right].Logit < heap[smallest].Logit) smallest = right;
            if (smallest == at) return;

            (heap[at], heap[smallest]) = (heap[smallest], heap[at]);
            at = smallest;
        }
    }
}

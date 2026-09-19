using System;
using System.Globalization;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner; //was previously: onnxruntime/contrib_ops/cpu/bert/gqa_attention_base.h@v1.30.0

/// <summary>
/// <c>GroupQueryAttention</c> [com.microsoft]: a whole decoder attention block in one operator - the rotary
/// embedding, the cache of everything seen so far, the causal mask, the softmax and both matrix products.
/// </summary>
/// <remarks>
/// <para>
/// THE SEMANTICS ARE PORTED, NOT RE-DERIVED, because three of them are counter-intuitive and each one gives a
/// model that runs and writes nonsense when it is guessed at:
/// </para>
/// <para>
/// <c>seqlens_k</c> IS NOT THE PAST LENGTH. It is the total length MINUS ONE - the index of the last valid
/// key, per batch - so the number of positions attended to is <c>seqlens_k[b] + 1</c> and the number of rows
/// to take from the cache is that minus the new positions.
/// </para>
/// <para>
/// <c>total_sequence_length</c> IS A SCALAR AND IT DECIDES WHAT KIND OF STEP THIS IS. When it equals the
/// query's own length the step is a first prompt, nothing is taken from the cache however long the cache is,
/// and the causal mask starts at nought; otherwise it is a continuation and the mask is offset by what came
/// before.
/// </para>
/// <para>
/// THE PRESENT CACHE IS AS LONG AS THE LONGER OF THE TWO - the total length, or the past buffer the caller
/// supplied - and the rows past what this step filled are left at nought. The scores are computed over the
/// valid columns only and the rest of the row is masked to nought after the softmax, so a longer buffer never
/// changes the answer.
/// </para>
/// <para>
/// GROUPED means several query heads share one key-and-value head: head <c>n</c> reads cache head
/// <c>n / (heads / kv_heads)</c>. The query may also arrive PACKED - one tensor holding the query, the key and
/// the value side by side, which is what the model builders emit - and then the key and value inputs are left
/// empty. Whichever way it arrives, the kernel lays it out head by head first, so everything after that reads
/// one layout.
/// </para>
/// <para>
/// Refused by name, each because it would change what the graph computes: a local window, a softcap, a smooth
/// softmax, a sliding-window cache, a quantized cache, a query or key normalization, an attention bias, a head
/// sink, caller-supplied position identifiers, and the score output. A model builder's exports carry the
/// first two as their defaults - a window of -1 and a cap of nought - and those are accepted, because they
/// mean the operator is not doing either.
/// </para>
/// </remarks>
internal sealed class OnnxGroupQueryAttentionKernel : OnnxKernel
{
    private const int Query = 0;
    private const int Key = 1;
    private const int Value = 2;
    private const int PastKey = 3;
    private const int PastValue = 4;
    private const int SequenceLengths = 5;
    private const int TotalSequenceLength = 6;
    private const int CosineCache = 7;
    private const int SineCache = 8;

    private static readonly string[] RefusedInputs =
    {
        "position_ids", "attention_bias", "head_sink", "k_scale", "v_scale", "q_norm_weight", "k_norm_weight",
    };

    /// <inheritdoc />
    internal override string OpType => "GroupQueryAttention";

    /// <inheritdoc />
    internal override string Domain => OnnxKernels.ContributedDomain;

    /// <inheritdoc />
    internal override int MinInputs => 7;

    /// <inheritdoc />
    internal override int MaxInputs => 16;

    /// <inheritdoc />
    internal override int MinOutputs => 1;

    /// <inheritdoc />
    internal override int MaxOutputs => 4;

    /// <inheritdoc />
    internal override string[] Attributes => new[]
    {
        "num_heads", "kv_num_heads", "scale", "causal", "do_rotary", "rotary_interleaved",
        "local_window_size", "softcap", "smooth_softmax", "sliding_window_cache", "kv_cache_bit_width",
        "k_quant_type", "v_quant_type", "qk_norm_epsilon", "qk_output",
    };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context)
    {
        long heads = context.Int("num_heads", 0);
        long keyValueHeads = context.Int("kv_num_heads", 0);
        if (heads <= 0 || keyValueHeads <= 0 || heads % keyValueHeads != 0)
        {
            throw context.Refuse(
                "its num_heads is " + heads.ToString(CultureInfo.InvariantCulture) + " and its kv_num_heads is "
                + keyValueHeads.ToString(CultureInfo.InvariantCulture)
                + "; both must be positive and the first must be a multiple of the second");
        }

        RequireDefault(context, "local_window_size", -1);
        RequireDefault(context, "smooth_softmax", 0);
        RequireDefault(context, "sliding_window_cache", 0);
        RequireDefault(context, "kv_cache_bit_width", 0);
        RequireDefault(context, "qk_output", 0);
        RequireDefault(context, "softcap", 0f);
        RequireNoQuantization(context, "k_quant_type");
        RequireNoQuantization(context, "v_quant_type");

        if (context.HasAttribute("qk_norm_epsilon"))
        {
            throw context.Refuse(
                "it carries qk_norm_epsilon, which normalizes the query and the key before the scores and this"
                + " engine does not implement");
        }

        for (int i = 0; i < RefusedInputs.Length; i++)
        {
            if (context.HasInput(SineCache + 1 + i))
            {
                throw context.Refuse(
                    "it supplies the optional input '" + RefusedInputs[i]
                    + "', which this engine does not implement");
            }
        }

        if (context.HasOutput(3))
        {
            throw context.Refuse("it asks for the score output, which this engine does not produce");
        }

        if (context.HasInput(Key) != context.HasInput(Value))
        {
            throw context.Refuse("its key and value must be both present or both absent");
        }

        if (context.HasInput(PastKey) != context.HasInput(PastValue))
        {
            throw context.Refuse("its past key and past value must be both present or both absent");
        }

        bool rotary = context.Int("do_rotary", 0) == 1;
        if (rotary != (context.HasInput(CosineCache) && context.HasInput(SineCache)))
        {
            throw context.Refuse(
                "it asks for a rotary embedding and carries no cosine and sine caches, or carries them and"
                + " does not ask for one");
        }

        return new OnnxAttentionSettings(
            (int)heads,
            (int)keyValueHeads,
            context.Float("scale", 0f),
            context.Int("causal", 1) == 1,
            rotary,
            context.Int("rotary_interleaved", 0) == 1);
    }

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxAttentionSettings settings = (OnnxAttentionSettings)context.State;
        OnnxValue query = Float(context, Query, "query");
        OnnxValue key = context.Input(Key) == null ? null : Float(context, Key, "key");
        OnnxValue value = context.Input(Value) == null ? null : Float(context, Value, "value");
        OnnxValue pastKey = context.Input(PastKey) == null ? null : Float(context, PastKey, "past key");
        OnnxValue pastValue = context.Input(PastValue) == null ? null : Float(context, PastValue, "past value");

        if (query.Rank != 3)
        {
            throw context.Fail(
                "its query " + OnnxShape.Describe(query.Shape) + " is not [batch, sequence, hidden].");
        }

        int batch = (int)query.Shape[0];
        int sequence = (int)query.Shape[1];
        int hidden = (int)query.Shape[2];
        bool packed = key == null;
        int lanes = settings.Heads + (2 * settings.KeyValueHeads);
        int queryLanes = packed ? lanes : settings.Heads;
        if (queryLanes == 0 || hidden % queryLanes != 0)
        {
            throw context.Fail(
                "its query is " + hidden.ToString(CultureInfo.InvariantCulture) + " wide, which does not"
                + " divide into " + queryLanes.ToString(CultureInfo.InvariantCulture) + " heads.");
        }

        int headSize = hidden / queryLanes;
        int[] lengths = Lengths(context, batch);
        int total = Total(context);
        int past = pastKey == null ? 0 : (int)pastKey.Shape[2];
        int present = Math.Max(total, past);
        bool prompt = sequence == total;

        Check(context, key, value, pastKey, pastValue, settings, batch, sequence, headSize, past);
        for (int b = 0; b < batch; b++)
        {
            if (lengths[b] < 0 || lengths[b] >= present)
            {
                throw context.Fail(
                    "its seqlens_k says the last key of batch " + b.ToString(CultureInfo.InvariantCulture)
                    + " is at " + lengths[b].ToString(CultureInfo.InvariantCulture)
                    + ", which is outside a cache of " + present.ToString(CultureInfo.InvariantCulture)
                    + " positions.");
            }

            if (!prompt && lengths[b] + 1 < sequence)
            {
                throw context.Fail(
                    "its seqlens_k is too small for a step of "
                    + sequence.ToString(CultureInfo.InvariantCulture) + " positions.");
            }
        }

        long[] presentShape = { batch, settings.KeyValueHeads, present, headSize };
        OnnxValue presentKey = context.AllocateOutput(1, OnnxElementType.Float, presentShape);
        OnnxValue presentValue =
            context.AllocateOutput(2, OnnxElementType.Float, (long[])presentShape.Clone());
        OnnxValue result = context.AllocateOutput(
            0, OnnxElementType.Float, new long[] { batch, sequence, settings.Heads * headSize });

        Array.Clear(presentKey.Floats, 0, presentKey.Count);
        Array.Clear(presentValue.Floats, 0, presentValue.Count);
        if (result.Count == 0) return;

        float[] laid = Lay(
            context, settings, query, key, value, batch, sequence, headSize, lanes, packed, lengths, prompt);

        Append(
            laid, presentKey.Floats, presentValue.Floats, pastKey, pastValue, settings,
            batch, sequence, headSize, lanes, past, present, lengths, prompt);

        Attend(
            context, settings, laid, presentKey.Floats, presentValue.Floats, result.Floats,
            batch, sequence, headSize, lanes, present, lengths, prompt);
    }

    private static void RequireDefault(OnnxNodeLoadContext context, string name, long expected)
    {
        long value = context.Int(name, expected);
        if (value != expected)
        {
            throw context.Refuse(
                "its " + name + " is " + value.ToString(CultureInfo.InvariantCulture)
                + " and this engine implements " + expected.ToString(CultureInfo.InvariantCulture) + " only");
        }
    }

    private static void RequireDefault(OnnxNodeLoadContext context, string name, float expected)
    {
        float value = context.Float(name, expected);
        if (value != expected)
        {
            throw context.Refuse(
                "its " + name + " is " + value.ToString(CultureInfo.InvariantCulture)
                + " and this engine implements " + expected.ToString(CultureInfo.InvariantCulture) + " only");
        }
    }

    private static void RequireNoQuantization(OnnxNodeLoadContext context, string name)
    {
        string value = context.Text(name);
        if (!string.IsNullOrEmpty(value) && !string.Equals(value, "NONE", StringComparison.OrdinalIgnoreCase))
        {
            throw context.Refuse(
                "its " + name + " is '" + value + "' and this engine keeps the cache in 32-bit floats");
        }
    }

    private static OnnxValue Float(OnnxOperatorContext context, int index, string what)
    {
        OnnxValue value = context.RequireInput(index);
        if (value.ElementType != OnnxElementType.Float)
        {
            throw context.Fail(
                "its " + what + " is " + OnnxTensor.Name(value.ElementType) + " and attention runs on floats.");
        }

        return value;
    }

    private static int[] Lengths(OnnxOperatorContext context, int batch)
    {
        OnnxValue value = context.RequireInput(SequenceLengths);
        if (value.ElementType != OnnxElementType.Int32 || value.Count != batch)
        {
            throw context.Fail(
                "its seqlens_k must hold one int32 for each of the "
                + batch.ToString(CultureInfo.InvariantCulture) + " batches.");
        }

        int[] lengths = new int[batch];
        Array.Copy(value.Int32s, lengths, batch);
        return lengths;
    }

    private static int Total(OnnxOperatorContext context)
    {
        OnnxValue value = context.RequireInput(TotalSequenceLength);
        if (value.ElementType != OnnxElementType.Int32 || value.Count != 1)
        {
            throw context.Fail("its total_sequence_length must be one int32.");
        }

        int total = value.Int32s[0];
        if (total <= 0)
        {
            throw context.Fail(
                "its total_sequence_length is " + total.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return total;
    }

    private static void Check(
        OnnxOperatorContext context, OnnxValue key, OnnxValue value, OnnxValue pastKey, OnnxValue pastValue,
        OnnxAttentionSettings settings, int batch, int sequence, int headSize, int past)
    {
        if (key != null
            && (key.Rank != 3 || key.Shape[0] != batch || key.Shape[1] != sequence
                || key.Shape[2] != (long)settings.KeyValueHeads * headSize
                || !Same(value.Shape, key.Shape)))
        {
            throw context.Fail(
                "its key " + OnnxShape.Describe(key.Shape) + " and value " + OnnxShape.Describe(value.Shape)
                + " are not both [batch, sequence, kv heads times head size].");
        }

        if (pastKey == null) return;

        long[] expected = { batch, settings.KeyValueHeads, past, headSize };
        if (!Same(pastKey.Shape, expected) || !Same(pastValue.Shape, expected))
        {
            throw context.Fail(
                "its past key " + OnnxShape.Describe(pastKey.Shape) + " and past value "
                + OnnxShape.Describe(pastValue.Shape)
                + " are not [batch, kv heads, cached positions, head size].");
        }
    }

    private static bool Same(long[] left, long[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return false;
        }

        return true;
    }

    /// <summary>
    /// Lays the query, the key and the value out head by head - [batch, lane, position, head size] - and turns
    /// the positions on the way. The value's lanes are copied as they are; only the query's and the key's are
    /// turned, which is what upstream does.
    /// </summary>
    private static float[] Lay(
        OnnxOperatorContext context, OnnxAttentionSettings settings, OnnxValue query, OnnxValue key,
        OnnxValue value, int batch, int sequence, int headSize, int lanes, bool packed, int[] lengths,
        bool prompt)
    {
        float[] target = new float[(long)batch * lanes * sequence * headSize];
        float[] cosine = null;
        float[] sine = null;
        int cacheWidth = 0;
        int cachePositions = 0;
        int rotaryWidth = 0;

        if (settings.Rotary)
        {
            OnnxValue cosineCache = Float(context, CosineCache, "cosine cache");
            OnnxValue sineCache = Float(context, SineCache, "sine cache");
            if (cosineCache.Rank != 2 || !Same(cosineCache.Shape, sineCache.Shape))
            {
                throw context.Fail("its cosine and sine caches must be the same [positions, half a head].");
            }

            cachePositions = (int)cosineCache.Shape[0];
            cacheWidth = (int)cosineCache.Shape[1];
            rotaryWidth = cacheWidth * 2;
            if (rotaryWidth > headSize)
            {
                throw context.Fail(
                    "its rotary caches are " + cacheWidth.ToString(CultureInfo.InvariantCulture)
                    + " wide, which is more than half a head of "
                    + headSize.ToString(CultureInfo.InvariantCulture) + ".");
            }

            cosine = cosineCache.Floats;
            sine = sineCache.Floats;
        }

        if (packed)
        {
            Copy(context, query.Floats, target, settings, batch, sequence, headSize, lanes, lanes, 0, 0,
                lanes, cosine, sine, cacheWidth, cachePositions, rotaryWidth, lengths, prompt,
                settings.Heads + settings.KeyValueHeads);
            return target;
        }

        Copy(context, query.Floats, target, settings, batch, sequence, headSize, lanes, settings.Heads, 0, 0,
            settings.Heads, cosine, sine, cacheWidth, cachePositions, rotaryWidth, lengths, prompt,
            settings.Heads);
        Copy(context, key.Floats, target, settings, batch, sequence, headSize, lanes, settings.KeyValueHeads,
            0, settings.Heads, settings.KeyValueHeads, cosine, sine, cacheWidth, cachePositions, rotaryWidth,
            lengths, prompt, settings.KeyValueHeads);
        Copy(context, value.Floats, target, settings, batch, sequence, headSize, lanes,
            settings.KeyValueHeads, 0, settings.Heads + settings.KeyValueHeads, settings.KeyValueHeads,
            cosine, sine, cacheWidth, cachePositions, rotaryWidth, lengths, prompt, 0);
        return target;
    }

    private static void Copy(
        OnnxOperatorContext context, float[] source, float[] target, OnnxAttentionSettings settings,
        int batch, int sequence, int headSize, int lanes, int sourceLanes, int firstSourceLane,
        int firstTargetLane, int count, float[] cosine, float[] sine, int cacheWidth, int cachePositions,
        int rotaryWidth, int[] lengths, bool prompt, int turnedLanes)
    {
        for (int b = 0; b < batch; b++)
        {
            int behind = prompt ? 0 : lengths[b] + 1 - sequence;
            for (int lane = 0; lane < count; lane++)
            {
                bool turn = settings.Rotary && lane < turnedLanes;
                for (int s = 0; s < sequence; s++)
                {
                    int from = (((b * sequence) + s) * sourceLanes * headSize)
                        + ((firstSourceLane + lane) * headSize);
                    int to = ((((b * lanes) + firstTargetLane + lane) * sequence) + s) * headSize;

                    if (!turn)
                    {
                        Array.Copy(source, from, target, to, headSize);
                        continue;
                    }

                    int position = behind + s;
                    if (position < 0 || position >= cachePositions)
                    {
                        throw context.Fail(
                            "position " + position.ToString(CultureInfo.InvariantCulture)
                            + " is outside the rotary caches, which hold "
                            + cachePositions.ToString(CultureInfo.InvariantCulture) + ".");
                    }

                    OnnxRotaryEmbedding.Rotate(
                        source, from, target, to, cosine, sine, position * cacheWidth, rotaryWidth,
                        settings.Interleaved);
                    if (rotaryWidth < headSize)
                    {
                        Array.Copy(
                            source, from + rotaryWidth, target, to + rotaryWidth, headSize - rotaryWidth);
                    }
                }
            }
        }
    }

    /// <summary>Copies what was cached and what is new into the present cache, one head at a time.</summary>
    private static void Append(
        float[] laid, float[] presentKey, float[] presentValue, OnnxValue pastKey, OnnxValue pastValue,
        OnnxAttentionSettings settings, int batch, int sequence, int headSize, int lanes, int past,
        int present, int[] lengths, bool prompt)
    {
        int keyLane = settings.Heads;
        int valueLane = settings.Heads + settings.KeyValueHeads;

        for (int b = 0; b < batch; b++)
        {
            int behind = pastKey == null || prompt ? 0 : lengths[b] + 1 - sequence;
            for (int head = 0; head < settings.KeyValueHeads; head++)
            {
                int target = ((b * settings.KeyValueHeads) + head) * present * headSize;
                if (behind > 0)
                {
                    int from = ((b * settings.KeyValueHeads) + head) * past * headSize;
                    Array.Copy(pastKey.Floats, from, presentKey, target, behind * headSize);
                    Array.Copy(pastValue.Floats, from, presentValue, target, behind * headSize);
                }

                int fresh = target + (behind * headSize);
                int keySource = (((b * lanes) + keyLane + head) * sequence) * headSize;
                int valueSource = (((b * lanes) + valueLane + head) * sequence) * headSize;
                Array.Copy(laid, keySource, presentKey, fresh, sequence * headSize);
                Array.Copy(laid, valueSource, presentValue, fresh, sequence * headSize);
            }
        }
    }

    /// <summary>The scores, the mask, the softmax and the weighted sum of the values.</summary>
    private static void Attend(
        OnnxOperatorContext context, OnnxAttentionSettings settings, float[] laid, float[] presentKey,
        float[] presentValue, float[] result, int batch, int sequence, int headSize, int lanes, int present,
        int[] lengths, bool prompt)
    {
        float alpha = settings.Scale == 0f ? 1f / MathF.Sqrt(headSize) : settings.Scale;
        int work = batch * settings.Heads;
        OnnxKernelKind kind = context.Settings.Kernel;
        int threads = context.Settings.Threads;

        long effort = (long)work * sequence * present * headSize;
        int workers = threads > 1 && effort >= OnnxGemm.ParallelThreshold
            ? Math.Min(threads, work)
            : 1;

        if (workers <= 1)
        {
            float[] scores = new float[(long)sequence * present];
            for (int i = 0; i < work; i++)
            {
                Head(i, scores, settings, laid, presentKey, presentValue, result, sequence, headSize, lanes,
                    present, lengths, prompt, alpha, kind);
            }

            return;
        }

        Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
        {
            float[] scores = new float[(long)sequence * present];
            for (int i = worker; i < work; i += workers)
            {
                Head(i, scores, settings, laid, presentKey, presentValue, result, sequence, headSize, lanes,
                    present, lengths, prompt, alpha, kind);
            }
        });
    }

    private static void Head(
        int index, float[] scores, OnnxAttentionSettings settings, float[] laid, float[] presentKey,
        float[] presentValue, float[] result, int sequence, int headSize, int lanes, int present,
        int[] lengths, bool prompt, float alpha, OnnxKernelKind kind)
    {
        int b = index / settings.Heads;
        int head = index % settings.Heads;
        int cacheHead = head / settings.HeadsPerKeyValueHead;
        int total = lengths[b] + 1;
        int behind = prompt ? 0 : total - sequence;

        int queryBase = (((b * lanes) + head) * sequence) * headSize;
        int cacheBase = ((b * settings.KeyValueHeads) + cacheHead) * present * headSize;

        for (int s = 0; s < sequence; s++)
        {
            int row = s * present;
            for (int t = 0; t < total; t++)
            {
                scores[row + t] = alpha * OnnxGemm.Dot(
                    laid, queryBase + (s * headSize), presentKey, cacheBase + (t * headSize), headSize, kind);
            }

            int visible = settings.Causal ? Math.Min(behind + s + 1, total) : total;
            Normalize(scores, row, visible);
            for (int t = visible; t < total; t++) scores[row + t] = 0f;

            int target = ((((b * sequence) + s) * settings.Heads) + head) * headSize;
            Array.Clear(result, target, headSize);
            for (int t = 0; t < total; t++)
            {
                float weight = scores[row + t];
                if (weight == 0f) continue;

                int from = cacheBase + (t * headSize);
                for (int h = 0; h < headSize; h++) result[target + h] += weight * presentValue[from + h];
            }
        }
    }

    private static void Normalize(float[] scores, int start, int length)
    {
        if (length <= 0) return;

        float largest = float.NegativeInfinity;
        for (int i = 0; i < length; i++)
        {
            if (scores[start + i] > largest) largest = scores[start + i];
        }

        float total = 0f;
        for (int i = 0; i < length; i++)
        {
            float value = MathF.Exp(scores[start + i] - largest);
            scores[start + i] = value;
            total += value;
        }

        float scale = 1f / total;
        for (int i = 0; i < length; i++) scores[start + i] *= scale;
    }
}

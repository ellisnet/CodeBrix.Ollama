using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// THE LOOP: the prompt read in one run, then one token at a time, each one appended to a cache the driver
/// owns and handed straight back to the graph.
/// </summary>
/// <remarks>
/// <para>
/// PREFILL AND DECODE ARE THE SAME RUN with different lengths. The first run is given every token of the
/// prompt at once, which is what makes it the expensive one; every run after it is given a single token and
/// the cache of everything before it. The engine is stateless, so what carries the conversation forward is
/// the cache, and it is fed back as the very same tensor instances with nothing copied.
/// </para>
/// <para>
/// THE ATTENTION MASK IS HOW THE GRAPH LEARNS ITS LENGTHS. The decoders this driver runs work out, inside the
/// graph, how many positions are in play and which the last one is - by totalling the mask and by taking its
/// width - so the mask a run is given has one entry per position, past and new alike. There is nothing else
/// to tell the graph how far along the generation is.
/// </para>
/// <para>
/// FOUR THINGS END A GENERATION: an end-of-sequence token, a stop sequence in the text, the request's token
/// limit, and the context filling up. Cancellation is honoured between steps; what has been handed over stays
/// handed over.
/// </para>
/// </remarks>
internal sealed class CausalLmGenerator
{
    private readonly IOnnxModel _model;
    private readonly CausalLmBundle _bundle;
    private readonly CausalLmDecoder _decoder;
    private readonly Gpt2ByteLevelTokenizer _tokenizer;
    private readonly CausalLmCache _cache;
    private readonly OnnxElementType _inputIdsType;
    private readonly OnnxElementType _attentionMaskType;
    private readonly OnnxElementType _positionIdsType;
    private readonly bool _hasAttentionMask;
    private readonly bool _hasPositionIds;
    private readonly HashSet<int> _endOfSequence = new HashSet<int>();

    /// <summary>Creates the loop over a loaded graph, and checks that the graph is one it can drive.</summary>
    /// <param name="model">The loaded graph.</param>
    /// <param name="bundle">What the bundle said about itself.</param>
    /// <param name="tokenizer">The tokenizer the bundle carries.</param>
    /// <exception cref="ModelLoadException">
    /// The graph declares an input this driver does not know how to fill, or declares one of its known inputs
    /// or its answer with an element type this driver does not produce.
    /// </exception>
    internal CausalLmGenerator(
        IOnnxModel model, CausalLmBundle bundle, Gpt2ByteLevelTokenizer tokenizer)
    {
        _model = model;
        _bundle = bundle;
        _decoder = bundle.Decoder;
        _tokenizer = tokenizer;
        _cache = CausalLmCache.ForModel(model, bundle.Decoder);

        foreach (int token in bundle.EndOfSequence) _endOfSequence.Add(token);

        HashSet<string> cache = _decoder.CacheNames();
        _inputIdsType = OnnxElementType.Int64;
        _attentionMaskType = OnnxElementType.Int64;
        _positionIdsType = OnnxElementType.Int64;

        foreach (OnnxValueMetadata input in model.Metadata.Inputs)
        {
            if (string.Equals(input.Name, _decoder.InputIds, StringComparison.Ordinal))
            {
                _inputIdsType = WholeNumber(input, "the token-number input");
                continue;
            }

            if (_decoder.AttentionMask != null
                && string.Equals(input.Name, _decoder.AttentionMask, StringComparison.Ordinal))
            {
                _attentionMaskType = WholeNumber(input, "the attention mask");
                _hasAttentionMask = true;
                continue;
            }

            if (_decoder.PositionIds != null
                && string.Equals(input.Name, _decoder.PositionIds, StringComparison.Ordinal))
            {
                _positionIdsType = WholeNumber(input, "the position input");
                _hasPositionIds = true;
                continue;
            }

            if (cache.Contains(input.Name)) continue;

            throw new ModelLoadException(
                "The graph declares the input '" + input.Name + "', which the generation configuration does"
                + " not name, so this driver does not know what to put in it.");
        }

        foreach (OnnxValueMetadata output in model.Metadata.Outputs)
        {
            if (!string.Equals(output.Name, _decoder.Logits, StringComparison.Ordinal)) continue;
            if (output.ElementType == OnnxElementType.Float) continue;

            throw new ModelLoadException(
                "The graph declares its answer '" + output.Name + "' as " + output.ElementType
                + ", and this driver reads scores as 32-bit floats.");
        }
    }

    /// <summary>How many positions the model's context holds.</summary>
    internal int ContextLength => _bundle.ContextLength;

    /// <summary>Generates from a prompt already turned into token numbers.</summary>
    /// <param name="prompt">The prompt's tokens.</param>
    /// <param name="plan">The settled request.</param>
    /// <param name="cancellationToken">A token to stop the generation between steps.</param>
    /// <returns>Updates as text is produced; the last one carries the reason and the statistics.</returns>
    internal async IAsyncEnumerable<GenerationUpdate> GenerateAsync(
        IReadOnlyList<int> prompt,
        CausalLmPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _cache.Reset();

        Utf8Assembler assembler = new Utf8Assembler();
        StopSequenceDetector stops = new StopSequenceDetector(plan.StopSequences);
        CausalLmSampler sampler = new CausalLmSampler(plan.Sampling, plan.Seed);
        List<int> pending = new List<int>();

        Stopwatch total = Stopwatch.StartNew();
        Stopwatch clock = Stopwatch.StartNew();

        float[] logits = await RunAsync(prompt, 0, cancellationToken).ConfigureAwait(false);
        clock.Stop();
        TimeSpan promptDuration = clock.Elapsed;

        int position = prompt.Count;
        int generated = 0;
        FinishReason reason = FinishReason.Stop;
        clock.Restart();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int token = sampler.Choose(logits);
            if (_endOfSequence.Contains(token))
            {
                //The token that ended the generation is not part of what it wrote, which is the same thing
                //the native half of this library does with an end-of-generation token.
                reason = FinishReason.Stop;
                break;
            }

            generated++;
            sampler.Accept(token);
            pending.Add(token);

            string emitted = stops.Append(assembler.Append(_tokenizer.Bytes(token, false)));
            if (emitted.Length > 0)
            {
                GenerationUpdate update = new GenerationUpdate
                {
                    Text = emitted,
                    Tokens = pending.ToArray(),
                };

                pending.Clear();
                yield return update;
            }

            if (stops.IsStopped)
            {
                reason = FinishReason.StopSequence;
                break;
            }

            if (plan.MaximumTokens.HasValue && generated >= plan.MaximumTokens.Value)
            {
                reason = FinishReason.Length;
                break;
            }

            if (position >= _bundle.ContextLength)
            {
                reason = FinishReason.ContextFull;
                break;
            }

            logits = await RunAsync(new[] { token }, position, cancellationToken).ConfigureAwait(false);
            position++;
        }

        clock.Stop();
        total.Stop();

        string tail = string.Empty;
        if (!stops.IsStopped)
        {
            tail = stops.Append(assembler.Flush());
            if (!stops.IsStopped) tail += stops.Flush();
        }
        else
        {
            assembler.Reset();
        }

        yield return new GenerationUpdate
        {
            Text = tail,
            Tokens = pending.ToArray(),
            IsFinal = true,
            FinishReason = reason,
            Statistics = new GenerationStatistics
            {
                PromptTokens = prompt.Count,
                CachedPromptTokens = 0,
                GeneratedTokens = generated,
                PromptDuration = promptDuration,
                GenerationDuration = clock.Elapsed,
                TotalDuration = total.Elapsed,
            },
        };
    }

    /// <summary>Checks that a prompt fits in the model's context before anything is run.</summary>
    /// <param name="prompt">The prompt's tokens.</param>
    /// <exception cref="InferenceException">It does not fit, or there is nothing to run at all.</exception>
    internal void RequireRunnable(IReadOnlyList<int> prompt)
    {
        if (prompt.Count == 0)
        {
            throw new InferenceException(
                "The prompt produced no tokens and the bundle names no beginning-of-sequence token to start"
                + " from, so there is nothing to run.");
        }

        if (prompt.Count > _bundle.ContextLength)
        {
            throw new InferenceException(
                "The prompt is " + prompt.Count.ToString(CultureInfo.InvariantCulture)
                + " tokens and this model's context holds "
                + _bundle.ContextLength.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }

    private static OnnxElementType WholeNumber(OnnxValueMetadata input, string what)
    {
        if (input.ElementType == OnnxElementType.Int64 || input.ElementType == OnnxElementType.Int32)
        {
            return input.ElementType;
        }

        throw new ModelLoadException(
            "The graph declares " + what + " '" + input.Name + "' as " + input.ElementType
            + ", and this driver writes whole numbers into it.");
    }

    private async Task<float[]> RunAsync(
        IReadOnlyList<int> tokens, int from, CancellationToken cancellationToken)
    {
        Dictionary<string, OnnxTensor> feeds = new Dictionary<string, OnnxTensor>(
            _cache.Count + 3, StringComparer.Ordinal);

        feeds[_decoder.InputIds] = Numbers(_inputIdsType, tokens, 1, tokens.Count);

        if (_hasAttentionMask)
        {
            //One entry per position, past and new alike: totalling it is how the graph works out which
            //position is the last valid one, and its width is how it works out the total.
            int[] ones = new int[from + tokens.Count];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1;
            feeds[_decoder.AttentionMask] = Numbers(_attentionMaskType, ones, 1, ones.Length);
        }

        if (_hasPositionIds)
        {
            int[] positions = new int[tokens.Count];
            for (int i = 0; i < positions.Length; i++) positions[i] = from + i;
            feeds[_decoder.PositionIds] = Numbers(_positionIdsType, positions, 1, positions.Length);
        }

        _cache.AddTo(feeds);

        IReadOnlyDictionary<string, OnnxTensor> outputs =
            await _model.RunAsync(feeds, cancellationToken).ConfigureAwait(false);

        _cache.Take(outputs, from + tokens.Count);

        if (!outputs.TryGetValue(_decoder.Logits, out OnnxTensor answer) || answer.Floats == null)
        {
            throw new InferenceException(
                "The run produced no '" + _decoder.Logits + "' of 32-bit floats, so there is no answer to"
                + " choose a token from.");
        }

        return LastRow(answer);
    }

    private static float[] LastRow(OnnxTensor answer)
    {
        long width = answer.Shape.Count == 0 ? answer.Count : answer.Shape[answer.Shape.Count - 1];
        if (width <= 0 || answer.Count < width)
        {
            throw new InferenceException(
                "The model's answer is " + answer + ", which holds no row of scores to choose from.");
        }

        //A graph may answer for every position of the run or only for its last one; either way the row that
        //decides the next token is the last one.
        int offset = (int)(answer.Count - width);
        float[] row = new float[width];
        Array.Copy(answer.Floats, offset, row, 0, (int)width);
        return row;
    }

    private static OnnxTensor Numbers(
        OnnxElementType elementType, IReadOnlyList<int> values, long rows, long columns)
    {
        if (elementType == OnnxElementType.Int32)
        {
            int[] narrow = new int[values.Count];
            for (int i = 0; i < values.Count; i++) narrow[i] = values[i];
            return OnnxTensor.FromInt32(narrow, rows, columns);
        }

        long[] wide = new long[values.Count];
        for (int i = 0; i < values.Count; i++) wide[i] = values[i];
        return OnnxTensor.FromInt64(wide, rows, columns);
    }
}

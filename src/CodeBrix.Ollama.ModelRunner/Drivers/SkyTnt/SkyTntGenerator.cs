using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner; //was previously: app_onnx.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// The two-graph generation loop: the BASE graph turns the events so far into one hidden state per event, and
/// the TOKEN graph turns that hidden state into the next event's tokens, one at a time, with a cache of its
/// own that is emptied for every event.
/// </summary>
/// <remarks>
/// <para>
/// WHY THERE ARE TWO GRAPHS. An event is a row of up to eight tokens - what kind it is, then its parameters -
/// and the expensive network reads and writes one hidden state per EVENT rather than per token. A small second
/// network then spells that state out as the row, so the cost of an event is one large step and about seven
/// small ones instead of eight large ones.
/// </para>
/// <para>
/// BOTH CACHES BELONG HERE. The base graph's cache grows by one position per event and is carried right
/// through the piece; the token graph's cache is emptied before each event and grows by one position per
/// token. Each graph's <c>present</c> output becomes the next step's <c>past</c> input, the same tensor
/// instance, with nothing copied.
/// </para>
/// <para>
/// EVENTS COME OUT AS THEY ARE MADE. Each one is handed to the caller the moment its last token is sampled -
/// never gathered up and handed over at the end - so a player can be started while the rest of the piece is
/// still being generated. Asking for cancellation stops the loop between token steps, and what has already
/// been handed over stays handed over.
/// </para>
/// <para>
/// WHAT THE MODEL MAY ANSWER IS NARROWED AT EVERY TOKEN. The first token of an event may only be one of the
/// six kinds of event or the ending token; the token after that may only be a value of whatever parameter the
/// kind puts first, and so on. The model was trained that way, and the masks are what make a row of tokens
/// always mean something.
/// </para>
/// </remarks>
internal sealed class SkyTntGenerator
{
    /// <summary>The name of the base graph's input holding the events so far.</summary>
    internal const string EventsInput = "x";

    /// <summary>The name of the base graph's output holding one state per event.</summary>
    internal const string HiddenOutput = "hidden";

    /// <summary>The name of the token graph's input taking one event's state.</summary>
    internal const string HiddenInput = "hidden";

    /// <summary>The name of the token graph's input taking the tokens of the event so far.</summary>
    internal const string TokensInput = "x";

    /// <summary>The name of the token graph's output holding the answer for the next token.</summary>
    internal const string LogitsOutput = "y";

    /// <summary>How many events of history the base graph is given, which is the window it was trained on.</summary>
    internal const int MaximumContextEvents = 4096;

    private readonly IOnnxModel _base;
    private readonly IOnnxModel _token;
    private readonly SkyTntTokenizer _tokenizer;
    private readonly int _hiddenSize;

    /// <summary>Creates the loop over two loaded graphs.</summary>
    /// <param name="baseModel">The graph that turns events into hidden states.</param>
    /// <param name="tokenModel">The graph that turns a hidden state into an event's tokens.</param>
    /// <param name="tokenizer">The tokenizer whose vocabulary both graphs were trained in.</param>
    /// <exception cref="ModelLoadException">A graph does not have the shape this driver drives.</exception>
    internal SkyTntGenerator(IOnnxModel baseModel, IOnnxModel tokenModel, SkyTntTokenizer tokenizer)
    {
        _base = baseModel;
        _token = tokenModel;
        _tokenizer = tokenizer;

        RequireInput(baseModel, EventsInput, "base");
        RequireInput(tokenModel, HiddenInput, "token");
        RequireInput(tokenModel, TokensInput, "token");
        _hiddenSize = HiddenSize(baseModel);
        RequireOutput(tokenModel, LogitsOutput, "token");
    }

    /// <summary>How wide one event's hidden state is.</summary>
    internal int HiddenWidth => _hiddenSize;

    /// <summary>
    /// Generates, handing each event over the moment it is finished.
    /// </summary>
    /// <param name="plan">What to generate and how.</param>
    /// <param name="cancellationToken">A token that stops the generation between token steps.</param>
    /// <returns>The events, as they are made.</returns>
    internal async IAsyncEnumerable<MidiEvent> GenerateAsync(
        SkyTntGenerationPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        int rowLength = _tokenizer.MaximumTokensPerEvent;
        List<int[]> sequence = Window(plan.PromptRows);
        GenerationRandom random = new GenerationRandom(plan.Seed);

        //The beat every event is placed on is a RUNNING TOTAL of the distances the events state, and the
        //prompt's own events move it along whether or not the caller asked to see them.
        int beat = 0;
        foreach (int[] row in sequence)
        {
            SkyTntEventRow decoded = _tokenizer.TokensToEvent(row);
            if (decoded == null) continue;

            beat += decoded.Values[0];
            if (!plan.IncludePromptEvents) continue;

            MidiEvent item = _tokenizer.ToMidiEvent(decoded, beat);
            if (item != null) yield return item.WithHorizon(SkyTntTokenizer.TickOf(beat, 0));
        }

        SkyTntCache baseCache = SkyTntCache.ForModel(_base, "base");
        SkyTntCache tokenCache = SkyTntCache.ForModel(_token, "token");
        OnnxTensor noHidden = OnnxTensor.FromFloats(Array.Empty<float>(), 1, 0, _hiddenSize);
        OnnxTensor noTokens = OnnxTensor.FromInt64(Array.Empty<long>(), 1, 0);

        int pastLength = 0;
        for (int produced = 0; produced < plan.MaximumEvents; produced++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Dictionary<string, OnnxTensor> feeds = new Dictionary<string, OnnxTensor>(StringComparer.Ordinal)
            {
                [EventsInput] = Events(sequence, pastLength, rowLength),
            };
            baseCache.AddTo(feeds);

            IReadOnlyDictionary<string, OnnxTensor> answered =
                await _base.RunAsync(feeds, cancellationToken).ConfigureAwait(false);
            baseCache.Take(answered);

            OnnxTensor hidden = LastState(answered);
            tokenCache.Reset();

            int[] row = new int[rowLength];
            for (int i = 0; i < rowLength; i++) row[i] = _tokenizer.PadId;

            bool ended = false;
            SkyTntEventType type = null;
            int length = 0;

            for (int i = 0; i < rowLength; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool[] allowed = SkyTntMask.Allowed(
                    _tokenizer, i, ended, type, plan.DisableProgramChange, plan.DisableControlChange,
                    plan.AllowedChannels);
                Dictionary<string, OnnxTensor> tokenFeeds =
                    new Dictionary<string, OnnxTensor>(StringComparer.Ordinal)
                    {
                        [HiddenInput] = i == 0 ? hidden : noHidden,
                        [TokensInput] = i == 0
                            ? noTokens
                            : OnnxTensor.FromInt64(new[] { (long)row[i - 1] }, 1, 1),
                    };
                tokenCache.AddTo(tokenFeeds);

                IReadOnlyDictionary<string, OnnxTensor> step =
                    await _token.RunAsync(tokenFeeds, cancellationToken).ConfigureAwait(false);
                tokenCache.Take(step);

                int token = SkyTntSampler.Sample(
                    Logits(step), allowed, plan.Temperature, plan.TopP, plan.TopK, random);
                row[i] = token;
                length = i + 1;

                if (i == 0)
                {
                    if (token == _tokenizer.EndId) ended = true;
                    else type = _tokenizer.TypeForToken(token);
                    continue;
                }

                if (ended) break;
                if (type != null && type.Parameters.Count == i) break;
            }

            sequence.Add(row);
            pastLength = sequence.Count - 1;

            if (!ended && length >= 2)
            {
                SkyTntEventRow decoded = _tokenizer.TokensToEvent(row);
                if (decoded != null)
                {
                    beat += decoded.Values[0];
                    MidiEvent item = _tokenizer.ToMidiEvent(decoded, beat);
                    if (item != null) yield return item.WithHorizon(SkyTntTokenizer.TickOf(beat, 0));
                }
            }

            if (ended) break;
        }
    }

    private static void RequireInput(IOnnxModel model, string name, string graph)
    {
        foreach (OnnxValueMetadata input in model.Metadata.Inputs)
        {
            if (string.Equals(input.Name, name, StringComparison.Ordinal)) return;
        }

        throw new ModelLoadException(
            "The " + graph + " graph does not take an input called '" + name + "', so it is not the "
            + graph + " graph of a MIDI model of this family.");
    }

    private static void RequireOutput(IOnnxModel model, string name, string graph)
    {
        foreach (OnnxValueMetadata output in model.Metadata.Outputs)
        {
            if (string.Equals(output.Name, name, StringComparison.Ordinal)) return;
        }

        throw new ModelLoadException(
            "The " + graph + " graph does not produce an output called '" + name + "', so it is not the "
            + graph + " graph of a MIDI model of this family.");
    }

    private static int HiddenSize(IOnnxModel model)
    {
        foreach (OnnxValueMetadata output in model.Metadata.Outputs)
        {
            if (!string.Equals(output.Name, HiddenOutput, StringComparison.Ordinal)) continue;
            if (output.Shape.Count != 3 || output.Shape[2].Length < 1) break;
            return (int)output.Shape[2].Length;
        }

        throw new ModelLoadException(
            "The base graph does not state the width of the '" + HiddenOutput + "' state it produces, and the"
            + " token graph has to be given one of nought length before the second token of every event.");
    }

    private static List<int[]> Window(List<int[]> rows)
    {
        List<int[]> window = new List<int[]>(rows.Count);
        int from = rows.Count > MaximumContextEvents ? rows.Count - MaximumContextEvents : 0;
        for (int i = from; i < rows.Count; i++) window.Add(rows[i]);
        return window;
    }

    private static OnnxTensor Events(List<int[]> sequence, int from, int rowLength)
    {
        int rows = sequence.Count - from;
        long[] values = new long[(long)rows * rowLength];
        int at = 0;
        for (int i = from; i < sequence.Count; i++)
        {
            int[] row = sequence[i];
            for (int j = 0; j < rowLength; j++) values[at++] = row[j];
        }

        return OnnxTensor.FromInt64(values, 1, rows, rowLength);
    }

    private OnnxTensor LastState(IReadOnlyDictionary<string, OnnxTensor> answered)
    {
        if (!answered.TryGetValue(HiddenOutput, out OnnxTensor hidden) || hidden.Floats == null)
        {
            throw new InferenceException(
                "The base graph produced no '" + HiddenOutput + "' state for the events it was given.");
        }

        //One state per event was produced and only the last one is asked about, because everything before it
        //has already been turned into tokens.
        long count = hidden.Count;
        if (count < _hiddenSize)
        {
            throw new InferenceException(
                "The base graph produced " + count.ToString(CultureInfo.InvariantCulture) + " numbers of"
                + " state where one event's state is " + _hiddenSize.ToString(CultureInfo.InvariantCulture)
                + ".");
        }

        float[] state = new float[_hiddenSize];
        Array.Copy(hidden.Floats, (int)(count - _hiddenSize), state, 0, _hiddenSize);
        return OnnxTensor.FromFloats(state, 1, 1, _hiddenSize);
    }

    private float[] Logits(IReadOnlyDictionary<string, OnnxTensor> step)
    {
        if (!step.TryGetValue(LogitsOutput, out OnnxTensor answer) || answer.Floats == null)
        {
            throw new InferenceException(
                "The token graph produced no '" + LogitsOutput + "' answer for the token it was asked about.");
        }

        int vocabulary = _tokenizer.VocabularySize;
        if (answer.Count < vocabulary || answer.Count % vocabulary != 0)
        {
            throw new InferenceException(
                "The token graph answered with " + answer.Count.ToString(CultureInfo.InvariantCulture)
                + " numbers, which is not a whole number of rows of "
                + vocabulary.ToString(CultureInfo.InvariantCulture) + ".");
        }

        //The answer for the LAST position, which is the one the next token is chosen from.
        if (answer.Count == vocabulary) return answer.Floats;

        float[] last = new float[vocabulary];
        Array.Copy(answer.Floats, (int)(answer.Count - vocabulary), last, 0, vocabulary);
        return last;
    }
}

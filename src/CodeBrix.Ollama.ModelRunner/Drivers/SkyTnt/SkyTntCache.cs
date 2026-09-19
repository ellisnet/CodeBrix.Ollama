using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner; //was previously: app_onnx.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// The key-and-value cache of one of the two graphs, which the DRIVER owns because the engine is stateless:
/// each step is given what the last one produced, and what comes out is passed straight back in.
/// </summary>
/// <remarks>
/// <para>
/// NOTHING IS COPIED. A graph's <c>present</c> output owns its array and the engine will not write to it
/// again, so the very same tensor becomes the next step's <c>past</c> input. On the larger of the two graphs
/// that cache runs into hundreds of megabytes a step, and copying it would cost more than the arithmetic.
/// </para>
/// <para>
/// A cache STARTS EMPTY rather than absent: the graph always takes its past inputs, and a first step hands it
/// tensors with a length of nought in the position axis. That is an ordinary tensor and every operator in the
/// engine handles it.
/// </para>
/// </remarks>
internal sealed class SkyTntCache
{
    private const string PastPrefix = "past_key_values";
    private const string PresentPrefix = "present";

    private readonly string[] _pastNames;
    private readonly string[] _presentNames;
    private readonly OnnxTensor[] _empty;
    private readonly OnnxTensor[] _held;

    private SkyTntCache(string[] pastNames, string[] presentNames, OnnxTensor[] empty)
    {
        _pastNames = pastNames;
        _presentNames = presentNames;
        _empty = empty;
        _held = new OnnxTensor[pastNames.Length];
        Reset();
    }

    /// <summary>How many tensors the cache is made of - two per layer, a key and a value.</summary>
    internal int Count => _pastNames.Length;

    /// <summary>
    /// Works out a graph's cache from what the graph says about itself: which of its inputs are past
    /// positions, what the output that replaces each one is called, and how wide each one is.
    /// </summary>
    /// <param name="model">The loaded graph.</param>
    /// <param name="graph">What to call the graph in a message.</param>
    /// <returns>The cache, empty.</returns>
    /// <exception cref="ModelLoadException">
    /// The graph declares a past input with no matching output, or one whose width it does not state.
    /// </exception>
    internal static SkyTntCache ForModel(IOnnxModel model, string graph)
    {
        List<string> pastNames = new List<string>();
        List<string> presentNames = new List<string>();
        List<OnnxTensor> empty = new List<OnnxTensor>();

        HashSet<string> outputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxValueMetadata output in model.Metadata.Outputs) outputs.Add(output.Name);

        foreach (OnnxValueMetadata input in model.Metadata.Inputs)
        {
            if (!input.Name.StartsWith(PastPrefix, StringComparison.Ordinal)) continue;

            string present = PresentPrefix + input.Name.Substring(PastPrefix.Length);
            if (!outputs.Contains(present))
            {
                throw new ModelLoadException(
                    "The " + graph + " graph takes '" + input.Name + "' and produces no '" + present
                    + "' to feed back into it, so its cache cannot be carried from one step to the next.");
            }

            if (input.Shape.Count != 4)
            {
                throw new ModelLoadException(
                    "The " + graph + " graph's '" + input.Name + "' has "
                    + input.Shape.Count.ToString(CultureInfo.InvariantCulture)
                    + " dimensions and a cache of this family has four: batch, heads, positions and width.");
            }

            long heads = input.Shape[1].Length;
            long width = input.Shape[3].Length;
            if (heads < 0 || width < 0)
            {
                throw new ModelLoadException(
                    "The " + graph + " graph leaves the shape of '" + input.Name + "' open, and a first step"
                    + " has to build an empty cache of exactly the right shape.");
            }

            pastNames.Add(input.Name);
            presentNames.Add(present);
            empty.Add(OnnxTensor.FromFloats(Array.Empty<float>(), 1, heads, 0, width));
        }

        if (pastNames.Count == 0)
        {
            throw new ModelLoadException(
                "The " + graph + " graph declares no past-position inputs, so it is not a decoder this driver"
                + " can step through.");
        }

        return new SkyTntCache(pastNames.ToArray(), presentNames.ToArray(), empty.ToArray());
    }

    /// <summary>Empties the cache, which is what the start of a piece - or of an event's tokens - needs.</summary>
    internal void Reset()
    {
        for (int i = 0; i < _held.Length; i++) _held[i] = _empty[i];
    }

    /// <summary>Adds what the cache holds to the tensors a step is about to be given.</summary>
    /// <param name="feeds">The step's inputs.</param>
    internal void AddTo(Dictionary<string, OnnxTensor> feeds)
    {
        for (int i = 0; i < _pastNames.Length; i++) feeds[_pastNames[i]] = _held[i];
    }

    /// <summary>Takes what a step produced, so that the next one continues from it.</summary>
    /// <param name="outputs">What the step produced.</param>
    /// <exception cref="InferenceException">A cache output the graph declares was not produced.</exception>
    internal void Take(IReadOnlyDictionary<string, OnnxTensor> outputs)
    {
        for (int i = 0; i < _presentNames.Length; i++)
        {
            if (!outputs.TryGetValue(_presentNames[i], out OnnxTensor tensor))
            {
                throw new InferenceException(
                    "The step produced no '" + _presentNames[i] + "', so its cache cannot be carried forward.");
            }

            _held[i] = tensor;
        }
    }
}

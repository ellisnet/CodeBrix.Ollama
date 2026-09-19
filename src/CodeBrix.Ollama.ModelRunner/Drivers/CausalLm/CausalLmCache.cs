using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The decoder's key-and-value cache, which the DRIVER owns because the engine is stateless: every step is
/// given what the last one produced, and what comes out is passed straight back in.
/// </summary>
/// <remarks>
/// <para>
/// NOTHING IS COPIED. A run's <c>present</c> output owns its array and the engine will not write to it again,
/// so the very same tensor instance becomes the next step's <c>past</c> input. On a model of any size the
/// cache is the largest thing moving between steps - two tensors per layer, growing by one position a token -
/// and copying it would cost more than the arithmetic does.
/// </para>
/// <para>
/// A cache STARTS EMPTY rather than absent: the graph always takes its past inputs, and the first run is
/// handed tensors with a length of nought in the position axis. That is an ordinary tensor and every operator
/// in the engine handles it.
/// </para>
/// <para>
/// THE NAMES AND THE SHAPE COME FROM THE BUNDLE. How many pairs there are, what each is called and how wide
/// each is are read out of the generation configuration; what the graph itself declares is then checked
/// against it, so a bundle whose configuration and graph disagree fails at load with a sentence rather than
/// at the first step with nonsense.
/// </para>
/// </remarks>
internal sealed class CausalLmCache
{
    private readonly string[] _pastNames;
    private readonly string[] _presentNames;
    private readonly OnnxTensor _empty;
    private readonly OnnxTensor[] _held;

    private CausalLmCache(string[] pastNames, string[] presentNames, OnnxTensor empty)
    {
        _pastNames = pastNames;
        _presentNames = presentNames;
        _empty = empty;
        _held = new OnnxTensor[pastNames.Length];
        Reset();
    }

    /// <summary>How many tensors the cache is made of - two per layer, a key and a value.</summary>
    internal int Count => _pastNames.Length;

    /// <summary>How many positions the cache holds, which is how much of the context is used.</summary>
    internal int Length { get; private set; }

    /// <summary>
    /// Works out the cache from what the bundle says and checks it against what the graph declares.
    /// </summary>
    /// <param name="model">The loaded graph.</param>
    /// <param name="decoder">What the generation configuration's decoder block said.</param>
    /// <returns>The cache, empty.</returns>
    /// <exception cref="ModelLoadException">
    /// The graph does not declare a cache tensor the configuration names, or declares one of a different
    /// shape or element type.
    /// </exception>
    internal static CausalLmCache ForModel(IOnnxModel model, CausalLmDecoder decoder)
    {
        Dictionary<string, OnnxValueMetadata> inputs = new Dictionary<string, OnnxValueMetadata>(
            StringComparer.Ordinal);
        foreach (OnnxValueMetadata input in model.Metadata.Inputs) inputs[input.Name] = input;

        HashSet<string> outputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxValueMetadata output in model.Metadata.Outputs) outputs.Add(output.Name);

        List<string> pastNames = new List<string>();
        List<string> presentNames = new List<string>();

        for (int layer = 0; layer < decoder.LayerCount; layer++)
        {
            Check(inputs, outputs, decoder, CausalLmDecoder.Name(decoder.PastKeyNames, layer),
                CausalLmDecoder.Name(decoder.PresentKeyNames, layer), pastNames, presentNames);
            Check(inputs, outputs, decoder, CausalLmDecoder.Name(decoder.PastValueNames, layer),
                CausalLmDecoder.Name(decoder.PresentValueNames, layer), pastNames, presentNames);
        }

        return new CausalLmCache(
            pastNames.ToArray(),
            presentNames.ToArray(),
            OnnxTensor.FromFloats(
                Array.Empty<float>(), 1, decoder.KeyValueHeadCount, 0, decoder.HeadSize));
    }

    /// <summary>Empties the cache, which is what the start of a generation needs.</summary>
    internal void Reset()
    {
        for (int i = 0; i < _held.Length; i++) _held[i] = _empty;
        Length = 0;
    }

    /// <summary>Adds what the cache holds to the tensors a run is about to be given.</summary>
    /// <param name="feeds">The run's inputs.</param>
    internal void AddTo(Dictionary<string, OnnxTensor> feeds)
    {
        for (int i = 0; i < _pastNames.Length; i++) feeds[_pastNames[i]] = _held[i];
    }

    /// <summary>Takes what a run produced, so that the next one continues from it.</summary>
    /// <param name="outputs">What the run produced.</param>
    /// <param name="length">How many positions the cache now holds.</param>
    /// <exception cref="InferenceException">A cache output the configuration names was not produced.</exception>
    internal void Take(IReadOnlyDictionary<string, OnnxTensor> outputs, int length)
    {
        for (int i = 0; i < _presentNames.Length; i++)
        {
            if (!outputs.TryGetValue(_presentNames[i], out OnnxTensor tensor))
            {
                throw new InferenceException(
                    "The run produced no '" + _presentNames[i] + "', so its cache cannot be carried forward.");
            }

            _held[i] = tensor;
        }

        Length = length;
    }

    private static void Check(
        Dictionary<string, OnnxValueMetadata> inputs,
        HashSet<string> outputs,
        CausalLmDecoder decoder,
        string past,
        string present,
        List<string> pastNames,
        List<string> presentNames)
    {
        if (!inputs.TryGetValue(past, out OnnxValueMetadata declared))
        {
            throw new ModelLoadException(
                "The generation configuration names the cache input '" + past + "' and the graph does not"
                + " declare it, so the two do not describe the same model.");
        }

        if (!outputs.Contains(present))
        {
            throw new ModelLoadException(
                "The graph takes '" + past + "' and produces no '" + present + "' to feed back into it, so"
                + " its cache cannot be carried from one step to the next.");
        }

        if (declared.ElementType != OnnxElementType.Float)
        {
            throw new ModelLoadException(
                "The graph declares the cache input '" + past + "' as "
                + declared.ElementType.ToString() + ", and this driver carries a cache of 32-bit floats.");
        }

        if (declared.Shape.Count != 4)
        {
            throw new ModelLoadException(
                "The graph declares '" + past + "' with "
                + declared.Shape.Count.ToString(CultureInfo.InvariantCulture)
                + " dimensions and a cache of this family has four: batch, heads, positions and width.");
        }

        Dimension(declared, past, 1, decoder.KeyValueHeadCount, "key and value heads");
        Dimension(declared, past, 3, decoder.HeadSize, "head size");

        pastNames.Add(past);
        presentNames.Add(present);
    }

    private static void Dimension(
        OnnxValueMetadata declared, string name, int axis, int expected, string what)
    {
        long stated = declared.Shape[axis].Length;

        //A dimension the graph leaves open is one it names rather than fixes, and the configuration is what
        //fills it in; one the graph FIXES has to agree with the configuration or the two describe different
        //models.
        if (stated < 0 || stated == expected) return;

        throw new ModelLoadException(
            "The graph declares '" + name + "' with " + stated.ToString(CultureInfo.InvariantCulture)
            + " for its " + what + " and the generation configuration states "
            + expected.ToString(CultureInfo.InvariantCulture) + ".");
    }
}

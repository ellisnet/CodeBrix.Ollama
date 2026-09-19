using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The decoder block of a bundle's generation configuration, read as it stands: the names the graph's tensors
/// go by, the patterns its per-layer cache tensors are numbered with, and the shape of the attention the
/// cache has to be built for.
/// </summary>
/// <remarks>
/// EVERYTHING THE DRIVER DOES IS DECIDED HERE. No name is assumed and no shape is guessed: the graph's input
/// is whatever the configuration calls <c>input_ids</c>, the cache is as many pairs as it states layers, and
/// each one is as wide as the head size it states. A bundle from another publisher that fills the same block
/// in differently is driven by the same code.
/// </remarks>
internal sealed class CausalLmDecoder
{
    /// <summary>Creates the decoder description.</summary>
    /// <param name="fileName">The graph file's name inside the bundle.</param>
    /// <param name="inputIds">The name of the input taking the token numbers.</param>
    /// <param name="attentionMask">The name of the input taking the attention mask, or <see langword="null"/>.</param>
    /// <param name="positionIds">The name of the input taking the positions, or <see langword="null"/>.</param>
    /// <param name="pastKeyNames">The pattern the per-layer past key inputs are named with.</param>
    /// <param name="pastValueNames">The pattern the per-layer past value inputs are named with.</param>
    /// <param name="logits">The name of the output carrying the answer.</param>
    /// <param name="presentKeyNames">The pattern the per-layer present key outputs are named with.</param>
    /// <param name="presentValueNames">The pattern the per-layer present value outputs are named with.</param>
    /// <param name="layerCount">How many layers, which is how many cache pairs there are.</param>
    /// <param name="headCount">How many attention heads.</param>
    /// <param name="keyValueHeadCount">How many key and value heads, which is the cache's second dimension.</param>
    /// <param name="headSize">How wide one head is, which is the cache's last dimension.</param>
    /// <param name="hiddenSize">The embedding width.</param>
    internal CausalLmDecoder(
        string fileName,
        string inputIds,
        string attentionMask,
        string positionIds,
        string pastKeyNames,
        string pastValueNames,
        string logits,
        string presentKeyNames,
        string presentValueNames,
        int layerCount,
        int headCount,
        int keyValueHeadCount,
        int headSize,
        int hiddenSize)
    {
        FileName = fileName;
        InputIds = inputIds;
        AttentionMask = attentionMask;
        PositionIds = positionIds;
        PastKeyNames = pastKeyNames;
        PastValueNames = pastValueNames;
        Logits = logits;
        PresentKeyNames = presentKeyNames;
        PresentValueNames = presentValueNames;
        LayerCount = layerCount;
        HeadCount = headCount;
        KeyValueHeadCount = keyValueHeadCount;
        HeadSize = headSize;
        HiddenSize = hiddenSize;
    }

    /// <summary>The graph file's name inside the bundle.</summary>
    internal string FileName { get; }

    /// <summary>The name of the input taking the token numbers.</summary>
    internal string InputIds { get; }

    /// <summary>The name of the input taking the attention mask, or <see langword="null"/> when there is none.</summary>
    internal string AttentionMask { get; }

    /// <summary>The name of the input taking the positions, or <see langword="null"/> when there is none.</summary>
    internal string PositionIds { get; }

    /// <summary>The pattern the per-layer past key inputs are named with, with <c>%d</c> for the layer.</summary>
    internal string PastKeyNames { get; }

    /// <summary>The pattern the per-layer past value inputs are named with, with <c>%d</c> for the layer.</summary>
    internal string PastValueNames { get; }

    /// <summary>The name of the output carrying the answer.</summary>
    internal string Logits { get; }

    /// <summary>The pattern the per-layer present key outputs are named with, with <c>%d</c> for the layer.</summary>
    internal string PresentKeyNames { get; }

    /// <summary>The pattern the per-layer present value outputs are named with, with <c>%d</c> for the layer.</summary>
    internal string PresentValueNames { get; }

    /// <summary>How many layers, which is how many key-and-value pairs the cache holds.</summary>
    internal int LayerCount { get; }

    /// <summary>How many attention heads.</summary>
    internal int HeadCount { get; }

    /// <summary>How many key and value heads, which is the cache's second dimension.</summary>
    internal int KeyValueHeadCount { get; }

    /// <summary>How wide one head is, which is the cache's last dimension.</summary>
    internal int HeadSize { get; }

    /// <summary>The embedding width.</summary>
    internal int HiddenSize { get; }

    /// <summary>The name one layer's tensor goes by.</summary>
    /// <param name="pattern">The pattern, with <c>%d</c> where the layer number goes.</param>
    /// <param name="layer">The layer.</param>
    /// <returns>The name.</returns>
    internal static string Name(string pattern, int layer) =>
        pattern.Replace("%d", layer.ToString(CultureInfo.InvariantCulture));

    /// <summary>Every name the decoder's cache is made of, past inputs and present outputs alike.</summary>
    /// <returns>The names, as a set.</returns>
    internal HashSet<string> CacheNames()
    {
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        for (int layer = 0; layer < LayerCount; layer++)
        {
            names.Add(Name(PastKeyNames, layer));
            names.Add(Name(PastValueNames, layer));
            names.Add(Name(PresentKeyNames, layer));
            names.Add(Name(PresentValueNames, layer));
        }

        return names;
    }
}

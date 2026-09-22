using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Inspectable text-to-attribute output, including confidence for every predicted category.</summary>
public sealed class MusicAttributePrediction
{
    internal MusicAttributePrediction(MusicAttributes attributes, Dictionary<string, IReadOnlyList<float>> probabilities,
        long[] tokenIds, bool wasTruncated)
    {
        Attributes = attributes;
        Probabilities = new ReadOnlyDictionary<string, IReadOnlyList<float>>(probabilities);
        TokenIds = Array.AsReadOnly((long[])tokenIds.Clone());
        WasTruncated = wasTruncated;
    }

    /// <summary>Predicted values, ready for inspection, immutable editing, reuse or music generation.</summary>
    public MusicAttributes Attributes { get; }

    /// <summary>Softmax probabilities by attribute name, ordered like each definition's values.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<float>> Probabilities { get; }

    /// <summary>The actual prompt token IDs, including CLS and SEP, after any truncation.</summary>
    public IReadOnlyList<long> TokenIds { get; }

    /// <summary>Whether the prompt exceeded the model's maximum sequence length and was truncated.</summary>
    public bool WasTruncated { get; }
}

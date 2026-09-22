using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>One categorical music attribute and its values in classifier order.</summary>
public sealed class MusicAttributeDefinition
{
    internal MusicAttributeDefinition(string name, string key, string[] values, string[] tokens, int defaultIndex, int classifier)
    {
        Name = name;
        Key = key;
        Values = System.Array.AsReadOnly(values);
        Tokens = System.Array.AsReadOnly(tokens);
        DefaultIndex = defaultIndex;
        Classifier = classifier;
    }

    /// <summary>The consumer name, for example <c>instrument.piano</c>, <c>tempo</c> or <c>key</c>.</summary>
    public string Name { get; }

    /// <summary>The identifier used by the original model, for example <c>I1s2_piano</c>.</summary>
    public string Key { get; }

    /// <summary>The allowed values, in the model's categorical order. The collection is immutable.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>The index used when a caller leaves this attribute unspecified.</summary>
    public int DefaultIndex { get; }

    /// <summary>Whether the optional text model predicts this attribute.</summary>
    public bool PredictedFromText => Classifier >= 0;

    internal IReadOnlyList<string> Tokens { get; }
    internal int Classifier { get; }
}

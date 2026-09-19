using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: midi_tokenizer.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// A small whole-number map that remembers the order its keys were first put in, which the tokenizer port
/// depends on: the upstream code numbers tracks by WALKING one of these, and the numbers it hands out decide
/// the order the tracks end up in.
/// </summary>
internal sealed class SkyTntOrderedIntMap
{
    private readonly Dictionary<int, int> _values = new Dictionary<int, int>();
    private readonly List<int> _keys = new List<int>();

    /// <summary>The keys, in the order they were first put in.</summary>
    internal IReadOnlyList<int> Keys => _keys;

    /// <summary>How many keys there are.</summary>
    internal int Count => _keys.Count;

    /// <summary>The value stored under a key.</summary>
    /// <param name="key">The key, which must be there.</param>
    /// <returns>The value.</returns>
    internal int this[int key] => _values[key];

    /// <summary>Whether a key is there.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal bool Contains(int key) => _values.ContainsKey(key);

    /// <summary>Stores a value, adding the key at the end when it is new.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void Set(int key, int value)
    {
        if (!_values.ContainsKey(key)) _keys.Add(key);
        _values[key] = value;
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Immutable, reusable categorical input for attribute-to-MIDI generation.</summary>
public sealed class MusicAttributes
{
    private readonly int[] _indices;

    internal MusicAttributes(MusicAttributeSchema schema, int[] indices)
    {
        Schema = schema;
        _indices = (int[])indices.Clone();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < indices.Length; i++)
        {
            MusicAttributeDefinition definition = schema.Definitions[i];
            values.Add(definition.Name, definition.Values[indices[i]]);
        }
        Values = new ReadOnlyDictionary<string, string>(values);
    }

    /// <summary>The schema these values belong to.</summary>
    public MusicAttributeSchema Schema { get; }

    /// <summary>Every attribute name and its selected value, including unspecified attributes.</summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    /// <summary>Returns a copy with one named value changed; the original remains reusable.</summary>
    /// <param name="name">An exact attribute name from <see cref="MusicAttributeSchema.Definitions"/>.</param>
    /// <param name="value">An exact category from that definition's values.</param>
    /// <returns>The edited value set.</returns>
    public MusicAttributes With(string name, string value)
    {
        int index = Schema.IndexOf(name);
        if (value == null) throw new ArgumentNullException(nameof(value));
        IReadOnlyList<string> choices = Schema.Definitions[index].Values;
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i] == value)
            {
                return WithIndex(name, i);
            }
        }
        throw new ArgumentException("Invalid value for '" + name + "'. Expected: " + string.Join(", ", choices), nameof(value));
    }

    /// <summary>Returns a copy selecting a category by its index in the schema.</summary>
    public MusicAttributes WithIndex(string name, int valueIndex)
    {
        int index = Schema.IndexOf(name);
        if (valueIndex < 0 || valueIndex >= Schema.Definitions[index].Values.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(valueIndex));
        }
        int[] copy = (int[])_indices.Clone();
        copy[index] = valueIndex;
        return new MusicAttributes(Schema, copy);
    }

    internal int IndexAt(int attribute) => _indices[attribute];
}

using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/exec.go (BSD-3-Clause);

/// <summary>
/// A Go map with string keys. Ranging over it and marshalling it visit the keys in sorted order, which
/// is what Go does. <see cref="Stringer"/> stands in for a map type that declares its own <c>String</c>
/// method.
/// </summary>
internal sealed class GoMap
{
    /// <summary>Creates an empty map.</summary>
    internal GoMap()
    {
    }

    /// <summary>The entries, keyed by name.</summary>
    internal Dictionary<string, object> Entries { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

    /// <summary>Whether this stands for a nil map rather than an empty one, which JSON renders as null.</summary>
    internal bool IsNil { get; set; }

    /// <summary>
    /// The value a lookup of an absent key produces - the zero value of the map's element type. It is
    /// <see cref="GoUndefined.Instance"/> for a map of <c>any</c>, which is what Ollama's
    /// <c>missingkey=zero</c> option amounts to there.
    /// </summary>
    internal object MissingValue { get; set; } = GoUndefined.Instance;

    /// <summary>The map type's own <c>String</c> method, when it has one.</summary>
    internal Func<GoMap, string> Stringer { get; set; }

    /// <summary>Adds or replaces an entry.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    internal void Set(string key, object value) => Entries[key] = value;

    /// <summary>Returns the keys in the sorted order Go visits them in.</summary>
    /// <returns>The sorted keys.</returns>
    internal List<string> SortedKeys()
    {
        List<string> keys = new List<string>(Entries.Keys);
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }
}

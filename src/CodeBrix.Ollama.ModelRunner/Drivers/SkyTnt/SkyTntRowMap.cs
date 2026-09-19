using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: midi_tokenizer.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// The collection the upstream tokenizer gathers events in, with the behaviour the port depends on: a
/// dictionary that REMEMBERS THE ORDER things were first put into it, where storing a key again replaces the
/// row WHERE IT ALREADY SAT rather than moving it to the end.
/// </summary>
/// <remarks>
/// It matters because the rows are sorted afterwards by a key that several events can share, and a stable
/// sort then leaves them in the order they were gathered. A plain dictionary in this language has no order at
/// all, and a list would turn "is this one already here" into a search.
/// </remarks>
internal sealed class SkyTntRowMap
{
    private readonly Dictionary<string, int> _positions = new Dictionary<string, int>();
    private readonly List<SkyTntEventRow> _rows = new List<SkyTntEventRow>();

    /// <summary>Stores a row under a key, replacing one already there without moving it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="row">The row.</param>
    internal void Set(string key, SkyTntEventRow row)
    {
        if (_positions.TryGetValue(key, out int at))
        {
            _rows[at] = row;
            return;
        }

        _positions[key] = _rows.Count;
        _rows.Add(row);
    }

    /// <summary>Takes a row out, leaving the ones around it where they are.</summary>
    /// <param name="key">The key.</param>
    internal void Remove(string key)
    {
        if (!_positions.TryGetValue(key, out int at)) return;
        _positions.Remove(key);
        _rows[at] = null;
    }

    /// <summary>The rows still in it, in the order they were first put in.</summary>
    /// <returns>The rows.</returns>
    internal List<SkyTntEventRow> Rows()
    {
        List<SkyTntEventRow> rows = new List<SkyTntEventRow>(_rows.Count);
        foreach (SkyTntEventRow row in _rows)
        {
            if (row != null) rows.Add(row);
        }

        return rows;
    }
}

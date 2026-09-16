using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The <c>loop</c> variable a <c>{% for %}</c> body sees. The sequence is materialized before the
/// loop starts - including any inline <c>if</c> filter - so <c>length</c>, <c>last</c>,
/// <c>previtem</c> and <c>nextitem</c> all describe the items that are actually visited.
/// </summary>
internal sealed class JinjaLoop
{
    private readonly IList<object> _items;

    private object _lastChanged = JinjaUndefined.Instance;

    /// <summary>Initializes a new instance of the <see cref="JinjaLoop"/> class.</summary>
    /// <param name="items">The items the loop visits.</param>
    /// <param name="depth">The one-based nesting depth.</param>
    internal JinjaLoop(IList<object> items, int depth)
    {
        _items = items;
        Depth = depth;
    }

    /// <summary>The zero-based index of the current item.</summary>
    internal int Index0 { get; set; }

    /// <summary>The one-based nesting depth.</summary>
    internal int Depth { get; }

    /// <summary>Reads one of the loop attributes.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or undefined for an unknown attribute.</returns>
    internal object Get(string name)
    {
        switch (name)
        {
            case "index":
                return (long)(Index0 + 1);
            case "index0":
                return (long)Index0;
            case "revindex":
                return (long)(_items.Count - Index0);
            case "revindex0":
                return (long)(_items.Count - Index0 - 1);
            case "first":
                return Index0 == 0;
            case "last":
                return Index0 == _items.Count - 1;
            case "length":
                return (long)_items.Count;
            case "depth":
                return (long)Depth;
            case "depth0":
                return (long)(Depth - 1);
            case "previtem":
                return Index0 > 0 ? _items[Index0 - 1] : JinjaUndefined.Named("previtem");
            case "nextitem":
                return Index0 + 1 < _items.Count ? _items[Index0 + 1] : JinjaUndefined.Named("nextitem");
            case "cycle":
            case "changed":
                return new JinjaBoundMethod(this, name);
            default:
                return JinjaUndefined.Named(name);
        }
    }

    /// <summary>Returns the argument at the current position in the cycle.</summary>
    /// <param name="arguments">The values to cycle through.</param>
    /// <returns>The chosen value, or undefined when no values were given.</returns>
    internal object Cycle(IList<object> arguments)
    {
        if (arguments == null || arguments.Count == 0)
        {
            return JinjaUndefined.Instance;
        }

        return arguments[Index0 % arguments.Count];
    }

    /// <summary>Reports whether the value differs from the one seen on the previous call.</summary>
    /// <param name="value">The value to compare.</param>
    /// <returns>True when the value changed.</returns>
    internal bool Changed(object value)
    {
        bool changed = !JinjaValues.AreEqual(_lastChanged, value);
        _lastChanged = value;
        return changed;
    }
}

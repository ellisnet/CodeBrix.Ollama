using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One link in the variable-scope chain. A <c>{% set %}</c> always writes into the innermost scope, a
/// read walks outwards, and a loop body gets a fresh scope per iteration - so, as in Jinja, an
/// assignment made inside a loop does not survive the iteration and <c>namespace()</c> is the way to
/// carry a value out.
/// </summary>
internal sealed class JinjaScope
{
    private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

    /// <summary>Initializes a new instance of the <see cref="JinjaScope"/> class.</summary>
    /// <param name="parent">The enclosing scope, or null for the root.</param>
    internal JinjaScope(JinjaScope parent)
    {
        Parent = parent;
    }

    /// <summary>The enclosing scope, or null.</summary>
    internal JinjaScope Parent { get; }

    /// <summary>Looks a name up through the chain.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">Receives the value when the name is bound.</param>
    /// <returns>True when the name was found.</returns>
    internal bool TryGet(string name, out object value)
    {
        for (JinjaScope scope = this; scope != null; scope = scope.Parent)
        {
            if (scope._values.TryGetValue(name, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Binds a name in this scope.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    internal void Set(string name, object value)
    {
        _values[name] = value;
    }
}

using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The object <c>namespace()</c> returns. Its attributes can be assigned with
/// <c>{% set ns.attribute = value %}</c>, which is how a template carries a value out of a loop.
/// </summary>
internal sealed class JinjaNamespace
{
    private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

    /// <summary>Reads an attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or undefined when the attribute was never set.</returns>
    internal object Get(string name)
    {
        return _values.TryGetValue(name, out object value) ? value : JinjaUndefined.Named(name);
    }

    /// <summary>Writes an attribute.</summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The value.</param>
    internal void Set(string name, object value)
    {
        _values[name] = value;
    }

    /// <summary>Renders the way Jinja's namespace object does.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() => "<Namespace>";
}

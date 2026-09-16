namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The value a missing variable, attribute or item evaluates to. It matches Jinja's default (non
/// strict) Undefined: falsy, renders as the empty string, fails the <c>defined</c> test and works with
/// the <c>default</c> filter, but cannot be iterated or used in arithmetic.
/// </summary>
internal sealed class JinjaUndefined
{
    /// <summary>The anonymous undefined value.</summary>
    internal static readonly JinjaUndefined Instance = new JinjaUndefined(null);

    private JinjaUndefined(string name)
    {
        Name = name;
    }

    /// <summary>The name that could not be resolved, or null when it is not known.</summary>
    internal string Name { get; }

    /// <summary>Creates an undefined value that remembers the name it stands for.</summary>
    /// <param name="name">The name that could not be resolved.</param>
    /// <returns>The undefined value.</returns>
    internal static JinjaUndefined Named(string name) => new JinjaUndefined(name);

    /// <summary>Describes the undefined value for an error message.</summary>
    /// <returns>Either <c>'name' is undefined</c> or a generic description.</returns>
    internal string Describe()
    {
        return Name == null ? "the value is undefined" : "'" + Name + "' is undefined";
    }

    /// <summary>Renders as the empty string, as Jinja's Undefined does.</summary>
    /// <returns>The empty string.</returns>
    public override string ToString() => string.Empty;
}

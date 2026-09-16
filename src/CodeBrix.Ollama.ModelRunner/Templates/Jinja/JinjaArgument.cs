namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One argument in a call or filter invocation, or one parameter in a macro signature. A null
/// <see cref="Name"/> means a positional argument; for a macro parameter the name is always set and
/// <see cref="Value"/> is the default, which may be null.
/// </summary>
internal sealed class JinjaArgument
{
    /// <summary>Initializes a new instance of the <see cref="JinjaArgument"/> class.</summary>
    /// <param name="name">The keyword or parameter name, or null for a positional argument.</param>
    /// <param name="value">The value expression, or the parameter default.</param>
    internal JinjaArgument(string name, JinjaExpression value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>The keyword or parameter name, or null.</summary>
    internal string Name { get; }

    /// <summary>The value expression.</summary>
    internal JinjaExpression Value { get; }
}

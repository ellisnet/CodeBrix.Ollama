using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A test, <c>value is name(args)</c> or <c>value is not name</c>.</summary>
internal sealed class JinjaTestExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaTestExpression"/> class.</summary>
    /// <param name="source">The tested expression.</param>
    /// <param name="name">The test name.</param>
    /// <param name="arguments">The extra arguments.</param>
    /// <param name="negated">True for <c>is not</c>.</param>
    internal JinjaTestExpression(
        JinjaExpression source, string name, IList<JinjaArgument> arguments, bool negated)
    {
        Source = source;
        Name = name;
        Arguments = arguments;
        Negated = negated;
    }

    /// <summary>The tested expression.</summary>
    internal JinjaExpression Source { get; }

    /// <summary>The test name.</summary>
    internal string Name { get; }

    /// <summary>The extra arguments.</summary>
    internal IList<JinjaArgument> Arguments { get; }

    /// <summary>True for <c>is not</c>.</summary>
    internal bool Negated { get; }
}

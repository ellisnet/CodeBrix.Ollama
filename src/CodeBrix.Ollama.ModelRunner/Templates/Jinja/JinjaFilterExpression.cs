using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A filter application, <c>value | name(args)</c>.</summary>
internal sealed class JinjaFilterExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaFilterExpression"/> class.</summary>
    /// <param name="source">The filtered expression, or null inside a <c>{% filter %}</c> block.</param>
    /// <param name="name">The filter name.</param>
    /// <param name="arguments">The extra arguments.</param>
    internal JinjaFilterExpression(JinjaExpression source, string name, IList<JinjaArgument> arguments)
    {
        Source = source;
        Name = name;
        Arguments = arguments;
    }

    /// <summary>The filtered expression, or null.</summary>
    internal JinjaExpression Source { get; }

    /// <summary>The filter name.</summary>
    internal string Name { get; }

    /// <summary>The extra arguments.</summary>
    internal IList<JinjaArgument> Arguments { get; }
}

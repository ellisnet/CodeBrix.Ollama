using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A call, <c>callee(a, b, key=c)</c>.</summary>
internal sealed class JinjaCallExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaCallExpression"/> class.</summary>
    /// <param name="callee">The expression that produces the callable.</param>
    /// <param name="arguments">The arguments, positional first then keyword.</param>
    internal JinjaCallExpression(JinjaExpression callee, IList<JinjaArgument> arguments)
    {
        Callee = callee;
        Arguments = arguments;
    }

    /// <summary>The expression that produces the callable.</summary>
    internal JinjaExpression Callee { get; }

    /// <summary>The arguments.</summary>
    internal IList<JinjaArgument> Arguments { get; }
}

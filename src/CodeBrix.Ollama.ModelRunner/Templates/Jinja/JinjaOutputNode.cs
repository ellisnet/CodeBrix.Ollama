namespace CodeBrix.Ollama.ModelRunner;

/// <summary>An output tag, <c>{{ expression }}</c>.</summary>
internal sealed class JinjaOutputNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaOutputNode"/> class.</summary>
    /// <param name="expression">The expression to render.</param>
    internal JinjaOutputNode(JinjaExpression expression)
    {
        Expression = expression;
    }

    /// <summary>The expression to render.</summary>
    internal JinjaExpression Expression { get; }
}

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A <c>{% do %}</c> statement: the expression is evaluated and its value discarded.</summary>
internal sealed class JinjaExpressionStatementNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaExpressionStatementNode"/> class.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    internal JinjaExpressionStatementNode(JinjaExpression expression)
    {
        Expression = expression;
    }

    /// <summary>The expression to evaluate.</summary>
    internal JinjaExpression Expression { get; }
}

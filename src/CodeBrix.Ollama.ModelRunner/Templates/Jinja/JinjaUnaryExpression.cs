namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A unary operation: <c>not x</c>, <c>-x</c> or <c>+x</c>.</summary>
internal sealed class JinjaUnaryExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaUnaryExpression"/> class.</summary>
    /// <param name="operation">The operator.</param>
    /// <param name="operand">The operand expression.</param>
    internal JinjaUnaryExpression(JinjaOperator operation, JinjaExpression operand)
    {
        Operation = operation;
        Operand = operand;
    }

    /// <summary>The operator.</summary>
    internal JinjaOperator Operation { get; }

    /// <summary>The operand expression.</summary>
    internal JinjaExpression Operand { get; }
}

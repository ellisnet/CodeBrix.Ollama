namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A binary operation, including the short-circuiting <c>and</c> and <c>or</c>.</summary>
internal sealed class JinjaBinaryExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaBinaryExpression"/> class.</summary>
    /// <param name="operation">The operator.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    internal JinjaBinaryExpression(JinjaOperator operation, JinjaExpression left, JinjaExpression right)
    {
        Operation = operation;
        Left = left;
        Right = right;
    }

    /// <summary>The operator.</summary>
    internal JinjaOperator Operation { get; }

    /// <summary>The left operand.</summary>
    internal JinjaExpression Left { get; }

    /// <summary>The right operand.</summary>
    internal JinjaExpression Right { get; }
}

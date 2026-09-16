namespace CodeBrix.Ollama.ModelRunner;

/// <summary>The inline conditional, <c>a if condition else b</c>.</summary>
internal sealed class JinjaConditionalExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaConditionalExpression"/> class.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="whenTrue">The value when the condition is truthy.</param>
    /// <param name="whenFalse">The value otherwise; null yields undefined, as Jinja does.</param>
    internal JinjaConditionalExpression(
        JinjaExpression condition, JinjaExpression whenTrue, JinjaExpression whenFalse)
    {
        Condition = condition;
        WhenTrue = whenTrue;
        WhenFalse = whenFalse;
    }

    /// <summary>The condition.</summary>
    internal JinjaExpression Condition { get; }

    /// <summary>The value when the condition is truthy.</summary>
    internal JinjaExpression WhenTrue { get; }

    /// <summary>The value when the condition is falsy, or null for undefined.</summary>
    internal JinjaExpression WhenFalse { get; }
}

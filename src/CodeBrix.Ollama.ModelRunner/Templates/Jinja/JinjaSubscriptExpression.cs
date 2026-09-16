namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Indexed access, <c>value[index]</c>.</summary>
internal sealed class JinjaSubscriptExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaSubscriptExpression"/> class.</summary>
    /// <param name="target">The expression the item is read from.</param>
    /// <param name="index">The index or key expression.</param>
    internal JinjaSubscriptExpression(JinjaExpression target, JinjaExpression index)
    {
        Target = target;
        Index = index;
    }

    /// <summary>The expression the item is read from.</summary>
    internal JinjaExpression Target { get; }

    /// <summary>The index or key expression.</summary>
    internal JinjaExpression Index { get; }
}

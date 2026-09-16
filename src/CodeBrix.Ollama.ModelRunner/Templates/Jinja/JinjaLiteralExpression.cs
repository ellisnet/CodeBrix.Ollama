namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A constant: a string, a number, a boolean, <c>none</c> or an <c>undefined</c> placeholder.</summary>
internal sealed class JinjaLiteralExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaLiteralExpression"/> class.</summary>
    /// <param name="value">The constant value.</param>
    internal JinjaLiteralExpression(object value)
    {
        Value = value;
    }

    /// <summary>The constant value.</summary>
    internal object Value { get; }
}

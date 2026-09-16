namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Dotted access, <c>value.name</c>.</summary>
internal sealed class JinjaAttributeExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaAttributeExpression"/> class.</summary>
    /// <param name="target">The expression the attribute is read from.</param>
    /// <param name="name">The attribute name.</param>
    internal JinjaAttributeExpression(JinjaExpression target, string name)
    {
        Target = target;
        Name = name;
    }

    /// <summary>The expression the attribute is read from.</summary>
    internal JinjaExpression Target { get; }

    /// <summary>The attribute name.</summary>
    internal string Name { get; }
}

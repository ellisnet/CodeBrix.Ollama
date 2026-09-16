namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A variable reference. An unresolved name evaluates to undefined.</summary>
internal sealed class JinjaNameExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaNameExpression"/> class.</summary>
    /// <param name="name">The variable name.</param>
    internal JinjaNameExpression(string name)
    {
        Name = name;
    }

    /// <summary>The variable name.</summary>
    internal string Name { get; }
}

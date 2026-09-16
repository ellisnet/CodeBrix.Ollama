namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Literal template text, written to the output unchanged.</summary>
internal sealed class JinjaTextNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaTextNode"/> class.</summary>
    /// <param name="text">The literal text.</param>
    internal JinjaTextNode(string text)
    {
        Text = text;
    }

    /// <summary>The literal text.</summary>
    internal string Text { get; }
}

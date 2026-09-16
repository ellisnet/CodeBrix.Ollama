namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A <c>{% break %}</c> or <c>{% continue %}</c> statement, from Jinja's loopcontrols extension.</summary>
internal sealed class JinjaLoopControlNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaLoopControlNode"/> class.</summary>
    /// <param name="isBreak">True for <c>break</c>, false for <c>continue</c>.</param>
    internal JinjaLoopControlNode(bool isBreak)
    {
        IsBreak = isBreak;
    }

    /// <summary>True for <c>break</c>, false for <c>continue</c>.</summary>
    internal bool IsBreak { get; }
}

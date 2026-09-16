using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Carries a <c>{% break %}</c> or <c>{% continue %}</c> out to the enclosing loop.</summary>
internal sealed class JinjaLoopSignal : Exception
{
    /// <summary>Initializes a new instance of the <see cref="JinjaLoopSignal"/> class.</summary>
    /// <param name="isBreak">True for <c>break</c>, false for <c>continue</c>.</param>
    internal JinjaLoopSignal(bool isBreak) : base(isBreak ? "break" : "continue")
    {
        IsBreak = isBreak;
    }

    /// <summary>True for <c>break</c>, false for <c>continue</c>.</summary>
    internal bool IsBreak { get; }
}

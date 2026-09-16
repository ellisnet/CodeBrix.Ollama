using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>One <c>if</c> or <c>elif</c> arm: a condition and the body it guards.</summary>
internal sealed class JinjaIfBranch
{
    /// <summary>Initializes a new instance of the <see cref="JinjaIfBranch"/> class.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="body">The body rendered when the condition is truthy.</param>
    internal JinjaIfBranch(JinjaExpression condition, IList<JinjaNode> body)
    {
        Condition = condition;
        Body = body;
    }

    /// <summary>The condition.</summary>
    internal JinjaExpression Condition { get; }

    /// <summary>The guarded body.</summary>
    internal IList<JinjaNode> Body { get; }
}

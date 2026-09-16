using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>An <c>{% if %}</c> statement with its <c>{% elif %}</c> arms and optional <c>{% else %}</c>.</summary>
internal sealed class JinjaIfNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaIfNode"/> class.</summary>
    /// <param name="branches">The <c>if</c> and <c>elif</c> arms, in order.</param>
    /// <param name="elseBody">The <c>else</c> body, or null.</param>
    internal JinjaIfNode(IList<JinjaIfBranch> branches, IList<JinjaNode> elseBody)
    {
        Branches = branches;
        ElseBody = elseBody;
    }

    /// <summary>The <c>if</c> and <c>elif</c> arms.</summary>
    internal IList<JinjaIfBranch> Branches { get; }

    /// <summary>The <c>else</c> body, or null.</summary>
    internal IList<JinjaNode> ElseBody { get; }
}

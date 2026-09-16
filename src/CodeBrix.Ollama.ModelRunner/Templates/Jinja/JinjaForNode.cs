using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A <c>{% for %}</c> loop, with optional tuple unpacking, an optional inline filter condition and an
/// optional <c>{% else %}</c> body that runs when the (filtered) sequence is empty.
/// </summary>
internal sealed class JinjaForNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaForNode"/> class.</summary>
    /// <param name="targets">The loop variable names.</param>
    /// <param name="sequence">The expression producing the sequence.</param>
    /// <param name="condition">The inline <c>if</c> filter, or null.</param>
    /// <param name="body">The loop body.</param>
    /// <param name="elseBody">The <c>else</c> body, or null.</param>
    internal JinjaForNode(
        IList<string> targets,
        JinjaExpression sequence,
        JinjaExpression condition,
        IList<JinjaNode> body,
        IList<JinjaNode> elseBody)
    {
        Targets = targets;
        Sequence = sequence;
        Condition = condition;
        Body = body;
        ElseBody = elseBody;
    }

    /// <summary>The loop variable names.</summary>
    internal IList<string> Targets { get; }

    /// <summary>The expression producing the sequence.</summary>
    internal JinjaExpression Sequence { get; }

    /// <summary>The inline <c>if</c> filter, or null.</summary>
    internal JinjaExpression Condition { get; }

    /// <summary>The loop body.</summary>
    internal IList<JinjaNode> Body { get; }

    /// <summary>The <c>else</c> body, or null.</summary>
    internal IList<JinjaNode> ElseBody { get; }
}

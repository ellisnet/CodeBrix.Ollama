using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A <c>{% set %}</c> statement, in both of its forms: an assignment from an expression, and a block
/// whose rendered body becomes the value.
/// </summary>
internal sealed class JinjaSetNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaSetNode"/> class.</summary>
    /// <param name="targets">The assignment targets; each is a dotted path with at least one part.</param>
    /// <param name="value">The value expression, or null for the block form.</param>
    /// <param name="body">The block body, or null for the expression form.</param>
    /// <param name="filter">A filter chain applied to the rendered block body, or null.</param>
    internal JinjaSetNode(
        IList<IList<string>> targets, JinjaExpression value, IList<JinjaNode> body, JinjaExpression filter)
    {
        Targets = targets;
        Value = value;
        Body = body;
        Filter = filter;
    }

    /// <summary>The assignment targets.</summary>
    internal IList<IList<string>> Targets { get; }

    /// <summary>The value expression, or null.</summary>
    internal JinjaExpression Value { get; }

    /// <summary>The block body, or null.</summary>
    internal IList<JinjaNode> Body { get; }

    /// <summary>A filter chain applied to the rendered block body, or null.</summary>
    internal JinjaExpression Filter { get; }
}

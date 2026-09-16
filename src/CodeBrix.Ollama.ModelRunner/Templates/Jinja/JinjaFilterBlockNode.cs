using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A <c>{% filter %}</c> block: the rendered body is passed through a filter chain.</summary>
internal sealed class JinjaFilterBlockNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaFilterBlockNode"/> class.</summary>
    /// <param name="filter">The filter chain, whose innermost source is null.</param>
    /// <param name="body">The body to render and filter.</param>
    internal JinjaFilterBlockNode(JinjaExpression filter, IList<JinjaNode> body)
    {
        Filter = filter;
        Body = body;
    }

    /// <summary>The filter chain.</summary>
    internal JinjaExpression Filter { get; }

    /// <summary>The body to render and filter.</summary>
    internal IList<JinjaNode> Body { get; }
}

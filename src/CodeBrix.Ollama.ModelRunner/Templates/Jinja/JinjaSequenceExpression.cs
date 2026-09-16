using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A list literal <c>[a, b]</c> or a tuple literal <c>(a, b)</c>; both evaluate to a list.</summary>
internal sealed class JinjaSequenceExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaSequenceExpression"/> class.</summary>
    /// <param name="items">The element expressions.</param>
    internal JinjaSequenceExpression(IList<JinjaExpression> items)
    {
        Items = items;
    }

    /// <summary>The element expressions.</summary>
    internal IList<JinjaExpression> Items { get; }
}

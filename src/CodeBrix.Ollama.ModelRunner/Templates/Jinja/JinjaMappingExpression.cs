using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A dictionary literal, <c>{'a': 1}</c>.</summary>
internal sealed class JinjaMappingExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaMappingExpression"/> class.</summary>
    /// <param name="keys">The key expressions.</param>
    /// <param name="values">The value expressions, in the same order as the keys.</param>
    internal JinjaMappingExpression(IList<JinjaExpression> keys, IList<JinjaExpression> values)
    {
        Keys = keys;
        Values = values;
    }

    /// <summary>The key expressions.</summary>
    internal IList<JinjaExpression> Keys { get; }

    /// <summary>The value expressions.</summary>
    internal IList<JinjaExpression> Values { get; }
}

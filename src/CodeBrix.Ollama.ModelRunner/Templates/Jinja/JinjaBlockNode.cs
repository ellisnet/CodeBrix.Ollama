using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A body that renders as-is. It backs <c>{% generation %}</c>, whose markers carry no meaning for
/// rendering, and <c>{% with %}</c>, which additionally opens a scope.
/// </summary>
internal sealed class JinjaBlockNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaBlockNode"/> class.</summary>
    /// <param name="body">The body.</param>
    /// <param name="newScope">True to render the body in a new scope.</param>
    internal JinjaBlockNode(IList<JinjaNode> body, bool newScope)
    {
        Body = body;
        NewScope = newScope;
    }

    /// <summary>The body.</summary>
    internal IList<JinjaNode> Body { get; }

    /// <summary>True to render the body in a new scope.</summary>
    internal bool NewScope { get; }
}

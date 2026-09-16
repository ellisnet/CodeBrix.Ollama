using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A <c>{% call %}</c> block: the body becomes a macro named <c>caller</c> that the called macro can
/// invoke.
/// </summary>
internal sealed class JinjaCallNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaCallNode"/> class.</summary>
    /// <param name="parameters">The parameters the caller block accepts.</param>
    /// <param name="call">The call expression.</param>
    /// <param name="body">The caller body.</param>
    internal JinjaCallNode(IList<JinjaArgument> parameters, JinjaCallExpression call, IList<JinjaNode> body)
    {
        Parameters = parameters;
        Call = call;
        Body = body;
    }

    /// <summary>The parameters the caller block accepts.</summary>
    internal IList<JinjaArgument> Parameters { get; }

    /// <summary>The call expression.</summary>
    internal JinjaCallExpression Call { get; }

    /// <summary>The caller body.</summary>
    internal IList<JinjaNode> Body { get; }
}

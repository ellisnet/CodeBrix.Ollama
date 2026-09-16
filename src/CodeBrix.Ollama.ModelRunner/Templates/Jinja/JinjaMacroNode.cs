using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A <c>{% macro %}</c> definition.</summary>
internal sealed class JinjaMacroNode : JinjaNode
{
    /// <summary>Initializes a new instance of the <see cref="JinjaMacroNode"/> class.</summary>
    /// <param name="name">The macro name.</param>
    /// <param name="parameters">The parameters, each with an optional default.</param>
    /// <param name="body">The macro body.</param>
    internal JinjaMacroNode(string name, IList<JinjaArgument> parameters, IList<JinjaNode> body)
        : this(name, parameters, body, true, true)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JinjaMacroNode"/> class.</summary>
    /// <param name="name">The macro name.</param>
    /// <param name="parameters">The parameters, each with an optional default.</param>
    /// <param name="body">The macro body.</param>
    /// <param name="acceptsVarargs">True when the body names <c>varargs</c>.</param>
    /// <param name="acceptsKeywords">True when the body names <c>kwargs</c>.</param>
    internal JinjaMacroNode(
        string name,
        IList<JinjaArgument> parameters,
        IList<JinjaNode> body,
        bool acceptsVarargs,
        bool acceptsKeywords)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
        AcceptsVarargs = acceptsVarargs;
        AcceptsKeywords = acceptsKeywords;
    }

    /// <summary>True when the body reads the implicit <c>varargs</c> list.</summary>
    internal bool AcceptsVarargs { get; }

    /// <summary>True when the body reads the implicit <c>kwargs</c> mapping.</summary>
    internal bool AcceptsKeywords { get; }

    /// <summary>The macro name.</summary>
    internal string Name { get; }

    /// <summary>The parameters.</summary>
    internal IList<JinjaArgument> Parameters { get; }

    /// <summary>The macro body.</summary>
    internal IList<JinjaNode> Body { get; }
}

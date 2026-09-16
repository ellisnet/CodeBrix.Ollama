namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/parse/node.go (BSD-3-Clause);

/// <summary>
/// The kind of a node in a parsed Go text/template.
/// </summary>
internal enum GoNodeType
{
    /// <summary>Plain text.</summary>
    Text = 0,

    /// <summary>A non-control action such as a field evaluation.</summary>
    Action,

    /// <summary>A boolean constant.</summary>
    Bool,

    /// <summary>A sequence of field accesses applied to a term.</summary>
    Chain,

    /// <summary>One element of a pipeline.</summary>
    Command,

    /// <summary>The cursor, dot.</summary>
    Dot,

    /// <summary>An <c>else</c> action. Never appears in a finished tree.</summary>
    Else,

    /// <summary>An <c>end</c> action. Never appears in a finished tree.</summary>
    End,

    /// <summary>A field or method name.</summary>
    Field,

    /// <summary>An identifier, which is always a function name.</summary>
    Identifier,

    /// <summary>An <c>if</c> action.</summary>
    If,

    /// <summary>A list of nodes.</summary>
    List,

    /// <summary>An untyped <c>nil</c> constant.</summary>
    Nil,

    /// <summary>A numeric constant.</summary>
    Number,

    /// <summary>A pipeline of commands.</summary>
    Pipe,

    /// <summary>A <c>range</c> action.</summary>
    Range,

    /// <summary>A string constant.</summary>
    String,

    /// <summary>A <c>template</c> invocation action.</summary>
    Template,

    /// <summary>A <c>$</c> variable.</summary>
    Variable,

    /// <summary>A <c>with</c> action.</summary>
    With,

    /// <summary>A comment.</summary>
    Comment,

    /// <summary>A <c>break</c> action.</summary>
    Break,

    /// <summary>A <c>continue</c> action.</summary>
    Continue,
}

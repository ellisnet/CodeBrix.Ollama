namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The kinds of token <see cref="JinjaLexer"/> produces. Template text arrives as
/// <see cref="Text"/>; everything else belongs to the inside of a <c>{{ }}</c> or <c>{% %}</c> tag.
/// </summary>
internal enum JinjaTokenKind
{
    /// <summary>Literal template text, already adjusted for whitespace control.</summary>
    Text,

    /// <summary>The opening <c>{{</c> of an output tag.</summary>
    VariableStart,

    /// <summary>The closing <c>}}</c> of an output tag.</summary>
    VariableEnd,

    /// <summary>The opening <c>{%</c> of a statement tag.</summary>
    BlockStart,

    /// <summary>The closing <c>%}</c> of a statement tag.</summary>
    BlockEnd,

    /// <summary>An identifier, which may also be a keyword such as <c>if</c> or <c>for</c>.</summary>
    Name,

    /// <summary>A string literal; the token value is the decoded text.</summary>
    String,

    /// <summary>An integer literal.</summary>
    Integer,

    /// <summary>A floating-point literal.</summary>
    Float,

    /// <summary>An operator or punctuator such as <c>+</c>, <c>|</c>, <c>(</c> or <c>==</c>.</summary>
    Operator,

    /// <summary>The end of the token stream.</summary>
    EndOfFile,
}

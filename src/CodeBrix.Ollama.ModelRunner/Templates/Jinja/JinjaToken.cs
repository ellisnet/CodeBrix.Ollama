namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One token from a Jinja template, carrying the index in the source it started at so that a parse
/// error can be reported with a line and a column.
/// </summary>
internal sealed class JinjaToken
{
    /// <summary>Initializes a new instance of the <see cref="JinjaToken"/> class.</summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="value">The token text: literal text, an identifier, a decoded string or an operator.</param>
    /// <param name="index">The zero-based index in the (whitespace-adjusted) source the token started at.</param>
    internal JinjaToken(JinjaTokenKind kind, string value, int index)
    {
        Kind = kind;
        Value = value;
        Index = index;
    }

    /// <summary>Initializes a new instance of the <see cref="JinjaToken"/> class for a number.</summary>
    /// <param name="kind">Either <see cref="JinjaTokenKind.Integer"/> or <see cref="JinjaTokenKind.Float"/>.</param>
    /// <param name="value">The literal text as it was written.</param>
    /// <param name="index">The zero-based index in the source the token started at.</param>
    /// <param name="number">The parsed numeric value, a <see cref="long"/> or a <see cref="double"/>.</param>
    internal JinjaToken(JinjaTokenKind kind, string value, int index, object number)
    {
        Kind = kind;
        Value = value;
        Index = index;
        Number = number;
    }

    /// <summary>The token kind.</summary>
    internal JinjaTokenKind Kind { get; }

    /// <summary>The token text, or null for the structural tokens.</summary>
    internal string Value { get; }

    /// <summary>The zero-based index in the source the token started at.</summary>
    internal int Index { get; }

    /// <summary>The parsed numeric value for a number token; otherwise null.</summary>
    internal object Number { get; }

    /// <summary>Tests whether this token is an operator with the given spelling.</summary>
    /// <param name="value">The operator spelling.</param>
    /// <returns>True when the token matches.</returns>
    internal bool IsOperator(string value) => Kind == JinjaTokenKind.Operator && Value == value;

    /// <summary>Tests whether this token is an identifier with the given spelling.</summary>
    /// <param name="value">The identifier.</param>
    /// <returns>True when the token matches.</returns>
    internal bool IsName(string value) => Kind == JinjaTokenKind.Name && Value == value;

    /// <summary>Returns a short description used in parse-error messages.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
    {
        switch (Kind)
        {
            case JinjaTokenKind.EndOfFile:
                return "end of template";
            case JinjaTokenKind.Text:
                return "template text";
            case JinjaTokenKind.VariableStart:
                return "'{{'";
            case JinjaTokenKind.VariableEnd:
                return "'}}'";
            case JinjaTokenKind.BlockStart:
                return "'{%'";
            case JinjaTokenKind.BlockEnd:
                return "'%}'";
            case JinjaTokenKind.String:
                return "a string literal";
            default:
                return "'" + Value + "'";
        }
    }
}

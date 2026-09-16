namespace CodeBrix.Ollama.ModelRunner;

/// <summary>The unary and binary operators the expression evaluator understands.</summary>
internal enum JinjaOperator
{
    /// <summary>Logical negation, <c>not</c>.</summary>
    Not,

    /// <summary>Arithmetic negation, unary <c>-</c>.</summary>
    Negate,

    /// <summary>Unary <c>+</c>.</summary>
    Positive,

    /// <summary>Addition, <c>+</c>.</summary>
    Add,

    /// <summary>Subtraction, <c>-</c>.</summary>
    Subtract,

    /// <summary>Multiplication, <c>*</c>.</summary>
    Multiply,

    /// <summary>True division, <c>/</c>.</summary>
    Divide,

    /// <summary>Floor division, <c>//</c>.</summary>
    FloorDivide,

    /// <summary>Modulo, <c>%</c>.</summary>
    Modulo,

    /// <summary>Exponentiation, <c>**</c>.</summary>
    Power,

    /// <summary>String concatenation, <c>~</c>.</summary>
    Concat,

    /// <summary>Equality, <c>==</c>.</summary>
    Equal,

    /// <summary>Inequality, <c>!=</c>.</summary>
    NotEqual,

    /// <summary>Less than, <c>&lt;</c>.</summary>
    Less,

    /// <summary>Less than or equal, <c>&lt;=</c>.</summary>
    LessOrEqual,

    /// <summary>Greater than, <c>&gt;</c>.</summary>
    Greater,

    /// <summary>Greater than or equal, <c>&gt;=</c>.</summary>
    GreaterOrEqual,

    /// <summary>Membership, <c>in</c>.</summary>
    In,

    /// <summary>Negated membership, <c>not in</c>.</summary>
    NotIn,

    /// <summary>Short-circuiting conjunction, <c>and</c>.</summary>
    And,

    /// <summary>Short-circuiting disjunction, <c>or</c>.</summary>
    Or,
}

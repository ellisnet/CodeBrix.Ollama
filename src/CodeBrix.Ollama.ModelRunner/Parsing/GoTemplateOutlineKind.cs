namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go text/template/parse (node kinds, reference only);

/// <summary>
/// The kind of a node in the structural outline of a Go text/template, mirroring the handful of
/// <c>text/template/parse</c> node types the Ollama heuristics look at.
/// </summary>
internal enum GoTemplateOutlineKind
{
    /// <summary>Literal text between actions.</summary>
    Text = 0,

    /// <summary>A plain <c>{{ ... }}</c> action, including variable assignments.</summary>
    Action = 1,

    /// <summary>An <c>{{ if ... }}</c> block.</summary>
    If = 2,

    /// <summary>A <c>{{ range ... }}</c> block.</summary>
    Range = 3,

    /// <summary>A <c>{{ with ... }}</c> block.</summary>
    With = 4,

    /// <summary>A <c>{{ template ... }}</c>, <c>{{ define ... }}</c> or <c>{{ block ... }}</c> node.</summary>
    Template = 5,
}

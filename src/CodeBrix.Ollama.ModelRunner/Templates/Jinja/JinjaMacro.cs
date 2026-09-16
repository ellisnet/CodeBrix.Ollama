namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A macro, closed over the scope it was defined in. Calling it renders its body and returns the text,
/// so a template can write <c>{{ render(x) }}</c> or <c>{% set y = render(x)|trim %}</c> alike.
/// </summary>
internal sealed class JinjaMacro
{
    /// <summary>Initializes a new instance of the <see cref="JinjaMacro"/> class.</summary>
    /// <param name="definition">The parsed definition.</param>
    /// <param name="closure">The scope the macro was defined in.</param>
    internal JinjaMacro(JinjaMacroNode definition, JinjaScope closure)
    {
        Definition = definition;
        Closure = closure;
    }

    /// <summary>The parsed definition.</summary>
    internal JinjaMacroNode Definition { get; }

    /// <summary>The scope the macro was defined in.</summary>
    internal JinjaScope Closure { get; }

    /// <summary>Renders the way Jinja's macro object does.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() => "<macro " + Definition.Name + ">";
}

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A Python-style slice, <c>value[start:stop:step]</c>, with any part optional.</summary>
internal sealed class JinjaSliceExpression : JinjaExpression
{
    /// <summary>Initializes a new instance of the <see cref="JinjaSliceExpression"/> class.</summary>
    /// <param name="target">The expression being sliced.</param>
    /// <param name="start">The start bound, or null when omitted.</param>
    /// <param name="stop">The stop bound, or null when omitted.</param>
    /// <param name="step">The step, or null when omitted.</param>
    internal JinjaSliceExpression(
        JinjaExpression target, JinjaExpression start, JinjaExpression stop, JinjaExpression step)
    {
        Target = target;
        Start = start;
        Stop = stop;
        Step = step;
    }

    /// <summary>The expression being sliced.</summary>
    internal JinjaExpression Target { get; }

    /// <summary>The start bound, or null.</summary>
    internal JinjaExpression Start { get; }

    /// <summary>The stop bound, or null.</summary>
    internal JinjaExpression Stop { get; }

    /// <summary>The step, or null.</summary>
    internal JinjaExpression Step { get; }
}

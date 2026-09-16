namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/exec.go (BSD-3-Clause);

/// <summary>
/// Stands in for Go's zero <c>reflect.Value</c>: the result of reading a key a map does not have, or a
/// field of nothing. Printing it yields <c>&lt;no value&gt;</c> and testing it for truth yields false.
/// </summary>
internal sealed class GoUndefined
{
    private GoUndefined()
    {
    }

    /// <summary>The single instance.</summary>
    internal static GoUndefined Instance { get; } = new GoUndefined();

    /// <summary>Renders the placeholder the way Go's template executor prints an invalid value.</summary>
    /// <returns>The text <c>&lt;no value&gt;</c>.</returns>
    public override string ToString() => "<no value>";
}

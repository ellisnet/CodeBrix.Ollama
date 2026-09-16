namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A global template function such as <c>range</c>, <c>namespace</c> or <c>raise_exception</c>.</summary>
internal sealed class JinjaFunction
{
    /// <summary>Initializes a new instance of the <see cref="JinjaFunction"/> class.</summary>
    /// <param name="name">The function name.</param>
    internal JinjaFunction(string name)
    {
        Name = name;
    }

    /// <summary>The function name.</summary>
    internal string Name { get; }

    /// <summary>Renders the way a Python function object does.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() => "<function " + Name + ">";
}

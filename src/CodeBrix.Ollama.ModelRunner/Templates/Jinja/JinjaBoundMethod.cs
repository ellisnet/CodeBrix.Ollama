namespace CodeBrix.Ollama.ModelRunner;

/// <summary>A method read from a value, such as <c>text.split</c> or <c>mapping.items</c>.</summary>
internal sealed class JinjaBoundMethod
{
    /// <summary>Initializes a new instance of the <see cref="JinjaBoundMethod"/> class.</summary>
    /// <param name="target">The value the method belongs to.</param>
    /// <param name="name">The method name.</param>
    internal JinjaBoundMethod(object target, string name)
    {
        Target = target;
        Name = name;
    }

    /// <summary>The value the method belongs to.</summary>
    internal object Target { get; }

    /// <summary>The method name.</summary>
    internal string Name { get; }

    /// <summary>Renders the way a Python bound method does.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() => "<bound method " + Name + ">";
}

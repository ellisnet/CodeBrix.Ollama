namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/funcs.go (BSD-3-Clause);

/// <summary>
/// One entry of a template's function table: the function itself plus the arity the executor checks
/// before calling it, which is what Go derives from the function's Go signature.
/// </summary>
internal sealed class GoFunction
{
    /// <summary>Creates a function table entry.</summary>
    /// <param name="name">The name templates call the function by.</param>
    /// <param name="minArgs">The smallest number of arguments the function accepts.</param>
    /// <param name="maxArgs">The largest number of arguments the function accepts, or -1 when it is variadic.</param>
    /// <param name="invoke">The function.</param>
    internal GoFunction(string name, int minArgs, int maxArgs, GoTemplateFunc invoke)
    {
        Name = name;
        MinArgs = minArgs;
        MaxArgs = maxArgs;
        Invoke = invoke;
    }

    /// <summary>The name templates call the function by.</summary>
    internal string Name { get; }

    /// <summary>The smallest number of arguments the function accepts.</summary>
    internal int MinArgs { get; }

    /// <summary>The largest number of arguments the function accepts; -1 when the function is variadic.</summary>
    internal int MaxArgs { get; }

    /// <summary>The function.</summary>
    internal GoTemplateFunc Invoke { get; }
}

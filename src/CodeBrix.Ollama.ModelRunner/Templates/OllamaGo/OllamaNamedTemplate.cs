namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama template/template.go;

/// <summary>
/// One entry of Ollama's <c>index.json</c>: the name of a built-in template and the chat template text -
/// typically a Jinja one lifted from a model file - that the built-in is the Go equivalent of.
/// </summary>
internal sealed class OllamaNamedTemplate
{
    /// <summary>Creates an index entry.</summary>
    /// <param name="name">The name of the built-in template.</param>
    /// <param name="template">The chat template text this entry is matched against.</param>
    internal OllamaNamedTemplate(string name, string template)
    {
        Name = name;
        Template = template;
    }

    /// <summary>The name of the built-in template.</summary>
    internal string Name { get; }

    /// <summary>The chat template text this entry is matched against.</summary>
    internal string Template { get; }
}

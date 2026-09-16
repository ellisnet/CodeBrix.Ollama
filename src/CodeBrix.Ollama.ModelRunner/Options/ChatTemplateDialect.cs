namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Which chat-template language renders the messages of a chat request into the prompt the model sees.
/// </summary>
public enum ChatTemplateDialect
{
    /// <summary>
    /// Choose from what is available: an Ollama template supplied in the options, otherwise the Jinja
    /// template embedded in the model file, otherwise the engine's built-in template matching. The default.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// The Jinja template the model file embeds (or the one supplied in <see cref="ModelRunnerOptions.JinjaTemplate"/>),
    /// rendered by this library's own Jinja engine.
    /// </summary>
    Jinja = 1,

    /// <summary>
    /// An Ollama Go text/template - the text of a Modelfile TEMPLATE, as CodeBrix.Ollama.ModelManager returns it
    /// - rendered by this library's own port of Ollama's template engine.
    /// </summary>
    Ollama = 2,

    /// <summary>
    /// The native engine's own template matching, which recognizes a fixed set of well-known template families
    /// and nothing else. A last resort.
    /// </summary>
    Native = 3,
}

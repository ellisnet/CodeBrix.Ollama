namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Options for <see cref="IModelStore.CreateAsync"/>.
/// </summary>
public sealed class CreateOptions
{
    /// <summary>
    /// The directory that relative paths in FROM and ADAPTER lines are resolved against. When
    /// <see langword="null"/>, the current working directory is used.
    /// </summary>
    public string BaseDirectory { get; set; }
}

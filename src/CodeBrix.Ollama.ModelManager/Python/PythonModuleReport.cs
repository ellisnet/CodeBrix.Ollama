namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What <see cref="PythonSupport.Check"/> found out about one Python module it was asked to look for.
/// </summary>
public sealed class PythonModuleReport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PythonModuleReport"/> class.
    /// </summary>
    /// <param name="name">The module name, as the caller asked for it.</param>
    /// <param name="isInstalled">Whether importing it succeeded.</param>
    /// <param name="error">What the import said when it failed; <see langword="null"/> when it worked.</param>
    internal PythonModuleReport(string name, bool isInstalled, string error)
    {
        Name = name;
        IsInstalled = isInstalled;
        Error = error;
    }

    /// <summary>
    /// The module name, as the caller asked for it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Whether the embedded interpreter could import the module.
    /// </summary>
    public bool IsInstalled { get; }

    /// <summary>
    /// What Python said when the import failed - typically <c>No module named '&lt;name&gt;'</c> - or
    /// <see langword="null"/> when the import succeeded.
    /// </summary>
    public string Error { get; }
}

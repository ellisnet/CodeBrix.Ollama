namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Where the virtual environment a <see cref="PythonSupportReport"/> describes came from.
/// </summary>
public enum PythonVirtualEnvironmentSource
{
    /// <summary>
    /// There is no virtual environment: the interpreter runs out of its own installation.
    /// </summary>
    None = 0,

    /// <summary>
    /// <see cref="PythonOptions.VirtualEnvironment"/>, set from code. This wins over everything else.
    /// </summary>
    Code = 1,

    /// <summary>
    /// The <c>CODEBRIX_OLLAMA_PYTHON_VENV</c> environment variable.
    /// </summary>
    EnvironmentVariable = 2,

    /// <summary>
    /// The environment the process was already launched in, which the embedded interpreter discovers for
    /// itself from <c>PYTHONNET_VENV</c> or <c>VIRTUAL_ENV</c>.
    /// </summary>
    Inherited = 3
}

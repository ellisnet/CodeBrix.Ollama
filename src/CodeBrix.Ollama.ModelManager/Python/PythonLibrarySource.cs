namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Where the CPython shared library a <see cref="PythonSupportReport"/> describes came from.
/// </summary>
public enum PythonLibrarySource
{
    /// <summary>
    /// No library was found at all: nothing was configured, no environment variable named one, and no
    /// virtual environment led to one.
    /// </summary>
    NotFound = 0,

    /// <summary>
    /// <see cref="PythonOptions.LibraryPath"/>, set from code. This wins over everything else.
    /// </summary>
    Code = 1,

    /// <summary>
    /// The <c>PYTHONNET_PYDLL</c> environment variable.
    /// </summary>
    EnvironmentVariable = 2,

    /// <summary>
    /// The base interpreter that the virtual environment's <c>pyvenv.cfg</c> records: its <c>home</c> and
    /// <c>version</c> keys name the installation the library is looked for in.
    /// </summary>
    VirtualEnvironment = 3,

    /// <summary>
    /// The interpreter the HOST APPLICATION started, which named its own CPython shared library in code.
    /// Nothing this library can read says where that library is, so the path is the one the running
    /// interpreter reports about itself rather than one that was resolved here.
    /// <see cref="PythonSupportReport.Owner"/> is <see cref="PythonEngineOwner.Host"/> whenever this is
    /// reported.
    /// </summary>
    Host = 4
}

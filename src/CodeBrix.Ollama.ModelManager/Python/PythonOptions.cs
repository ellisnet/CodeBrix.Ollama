namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Where the Python features of this library find CPython. Every property is <see langword="null"/> by
/// default, which leaves the two environment variables named below - and, failing those, whatever the
/// process already has - to answer the question.
/// <para>
/// Nothing here is read until a Python feature is used. Obtaining, listing, resolving and materializing
/// models never look at it.
/// </para>
/// </summary>
public sealed class PythonOptions
{
    /// <summary>
    /// The environment variable consulted for <see cref="VirtualEnvironment"/> when the property is not
    /// set: <c>CODEBRIX_OLLAMA_PYTHON_VENV</c>.
    /// </summary>
    public const string VirtualEnvironmentVariable = "CODEBRIX_OLLAMA_PYTHON_VENV";

    /// <summary>
    /// The environment variable consulted for <see cref="LibraryPath"/> when the property is not set:
    /// <c>PYTHONNET_PYDLL</c>.
    /// </summary>
    public const string LibraryPathVariable = "PYTHONNET_PYDLL";

    /// <summary>
    /// The directory of the Python virtual environment the embedded interpreter runs out of - the folder
    /// holding <c>pyvenv.cfg</c>, <c>bin/python</c> (<c>Scripts\python.exe</c> on Windows) and
    /// <c>site-packages</c>. The modules a Python feature needs must be installed there.
    /// <para>
    /// <see langword="null"/> falls back to the <c>CODEBRIX_OLLAMA_PYTHON_VENV</c> environment variable,
    /// and then to whatever the process itself already names (<c>PYTHONNET_VENV</c> or
    /// <c>VIRTUAL_ENV</c>). A value set here wins over all of them.
    /// </para>
    /// </summary>
    public string VirtualEnvironment { get; set; }

    /// <summary>
    /// The full path of the CPython shared library to embed - <c>libpython3.XX.so</c> on Linux,
    /// <c>python3XX.dll</c> on Windows, <c>libpython3.XX.dylib</c> on macOS.
    /// <para>
    /// <see langword="null"/> falls back to the <c>PYTHONNET_PYDLL</c> environment variable, and then to
    /// the library belonging to the base interpreter that <see cref="VirtualEnvironment"/>'s
    /// <c>pyvenv.cfg</c> records. A value set here wins over both.
    /// </para>
    /// </summary>
    public string LibraryPath { get; set; }
}

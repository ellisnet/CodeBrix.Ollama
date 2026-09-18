using System.IO;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when a feature that needs a particular Python module is asked for and that module cannot be
/// imported by the embedded interpreter. The message names the interpreter it looked in, the virtual
/// environment if there is one, the command that installs the module THERE, and the feature that needed
/// it.
/// </summary>
public class PythonModuleNotInstalledException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PythonModuleNotInstalledException"/> class.
    /// </summary>
    /// <param name="feature">The feature that needed the module, named as a phrase - "exporting to ONNX".</param>
    /// <param name="moduleName">The module that would not import - "onnxruntime".</param>
    /// <param name="libraryPath">The CPython shared library the interpreter is running, or <see langword="null"/>.</param>
    /// <param name="virtualEnvironment">The virtual environment in effect, or <see langword="null"/> when there is none.</param>
    public PythonModuleNotInstalledException(string feature, string moduleName, string libraryPath, string virtualEnvironment)
        : base(BuildMessage(feature, moduleName, libraryPath, virtualEnvironment))
    {
        Feature = feature;
        ModuleName = moduleName;
    }

    /// <summary>
    /// The feature that needed the module, as the caller named it.
    /// </summary>
    public string Feature { get; }

    /// <summary>
    /// The module that would not import.
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// The command that installs a module into the interpreter the message names: the virtual
    /// environment's own pip when there is one, and a plain <c>pip install</c> when there is not.
    /// </summary>
    /// <param name="moduleName">The module to install.</param>
    /// <param name="virtualEnvironment">The virtual environment, or <see langword="null"/>.</param>
    /// <returns>A command line that can be copied and run as it stands.</returns>
    public static string InstallCommand(string moduleName, string virtualEnvironment)
    {
        string module = string.IsNullOrWhiteSpace(moduleName) ? "<module>" : moduleName.Trim();
        if (string.IsNullOrWhiteSpace(virtualEnvironment))
        {
            return "pip install " + module;
        }
        string pip = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(virtualEnvironment.Trim(), "Scripts", "pip.exe")
            : Path.Combine(virtualEnvironment.Trim(), "bin", "pip");
        return pip + " install " + module;
    }

    /// <summary>
    /// Composes the message: which module, which interpreter, which environment, how to install it and
    /// what needed it.
    /// </summary>
    /// <param name="feature">The feature that needed the module.</param>
    /// <param name="moduleName">The module that would not import.</param>
    /// <param name="libraryPath">The CPython shared library in use, or <see langword="null"/>.</param>
    /// <param name="virtualEnvironment">The virtual environment, or <see langword="null"/>.</param>
    /// <returns>The complete message.</returns>
    private static string BuildMessage(string feature, string moduleName, string libraryPath, string virtualEnvironment)
    {
        string module = string.IsNullOrWhiteSpace(moduleName) ? "<module>" : moduleName.Trim();
        string library = string.IsNullOrWhiteSpace(libraryPath) ? "(unknown path)" : libraryPath.Trim();
        string environment = string.IsNullOrWhiteSpace(virtualEnvironment)
            ? "no virtual environment"
            : "virtual environment " + virtualEnvironment.Trim();
        string needed = string.IsNullOrWhiteSpace(feature)
            ? string.Empty
            : " It is needed for " + feature.Trim() + ".";
        return "The Python module '" + module + "' is not installed in the CPython at " + library
               + " (" + environment + "). Install it there with: "
               + InstallCommand(module, virtualEnvironment) + needed;
    }
}

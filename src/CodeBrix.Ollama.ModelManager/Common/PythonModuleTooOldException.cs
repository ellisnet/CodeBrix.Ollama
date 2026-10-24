using System.IO;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Thrown when a feature that needs a particular Python module is asked for and the module IS installed,
/// but at a version older than the lowest this library will run - a floor that exists only where
/// earlier releases carry a known hole a hostile model can reach. The message names the module, the
/// installed and required versions, the interpreter and virtual environment, the command that upgrades
/// the module THERE, and the feature that needed it.
/// </summary>
public class PythonModuleTooOldException : ModelManagerException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PythonModuleTooOldException"/> class.
    /// </summary>
    /// <param name="feature">The feature that needed the module, named as a phrase - "exporting to ONNX".</param>
    /// <param name="moduleName">The module that is too old - "torch".</param>
    /// <param name="installedVersion">The version it reports, or <see langword="null"/> when it could not be read.</param>
    /// <param name="minimumVersion">The lowest version this library runs - "2.6".</param>
    /// <param name="libraryPath">The CPython shared library the interpreter is running, or <see langword="null"/>.</param>
    /// <param name="virtualEnvironment">The virtual environment in effect, or <see langword="null"/> when there is none.</param>
    public PythonModuleTooOldException(
        string feature,
        string moduleName,
        string installedVersion,
        string minimumVersion,
        string libraryPath,
        string virtualEnvironment)
        : base(BuildMessage(feature, moduleName, installedVersion, minimumVersion, libraryPath, virtualEnvironment))
    {
        Feature = feature;
        ModuleName = moduleName;
        InstalledVersion = installedVersion;
        MinimumVersion = minimumVersion;
    }

    /// <summary>
    /// The feature that needed the module, as the caller named it.
    /// </summary>
    public string Feature { get; }

    /// <summary>
    /// The module that is too old.
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// The version the module reports, or <see langword="null"/> when it could not be read.
    /// </summary>
    public string InstalledVersion { get; }

    /// <summary>
    /// The lowest version of the module this library runs.
    /// </summary>
    public string MinimumVersion { get; }

    /// <summary>
    /// The command that upgrades a module in the interpreter the message names: the virtual
    /// environment's own pip when there is one, and a plain <c>pip install</c> when there is not.
    /// </summary>
    /// <param name="moduleName">The module to upgrade.</param>
    /// <param name="minimumVersion">The lowest version wanted.</param>
    /// <param name="virtualEnvironment">The virtual environment, or <see langword="null"/>.</param>
    /// <returns>A command line that can be copied and run as it stands.</returns>
    public static string UpgradeCommand(string moduleName, string minimumVersion, string virtualEnvironment)
    {
        string module = string.IsNullOrWhiteSpace(moduleName) ? "<module>" : moduleName.Trim();
        string requirement = string.IsNullOrWhiteSpace(minimumVersion)
            ? module
            : "\"" + module + ">=" + minimumVersion.Trim() + "\"";
        string pip = "pip";
        if (!string.IsNullOrWhiteSpace(virtualEnvironment))
        {
            pip = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(virtualEnvironment.Trim(), "Scripts", "pip.exe")
                : Path.Combine(virtualEnvironment.Trim(), "bin", "pip");
        }
        return pip + " install --upgrade " + requirement;
    }

    /// <summary>
    /// Composes the message: which module, which versions, which interpreter, which environment, how to
    /// upgrade it and what needed it.
    /// </summary>
    /// <param name="feature">The feature that needed the module.</param>
    /// <param name="moduleName">The module that is too old.</param>
    /// <param name="installedVersion">The version it reports, or <see langword="null"/>.</param>
    /// <param name="minimumVersion">The lowest version this library runs.</param>
    /// <param name="libraryPath">The CPython shared library in use, or <see langword="null"/>.</param>
    /// <param name="virtualEnvironment">The virtual environment, or <see langword="null"/>.</param>
    /// <returns>The complete message.</returns>
    private static string BuildMessage(
        string feature,
        string moduleName,
        string installedVersion,
        string minimumVersion,
        string libraryPath,
        string virtualEnvironment)
    {
        string module = string.IsNullOrWhiteSpace(moduleName) ? "<module>" : moduleName.Trim();
        string installed = string.IsNullOrWhiteSpace(installedVersion)
            ? "of a version that could not be read"
            : "version " + installedVersion.Trim();
        string minimum = string.IsNullOrWhiteSpace(minimumVersion) ? "(unknown)" : minimumVersion.Trim();
        string library = string.IsNullOrWhiteSpace(libraryPath) ? "(unknown path)" : libraryPath.Trim();
        string environment = string.IsNullOrWhiteSpace(virtualEnvironment)
            ? "no virtual environment"
            : "virtual environment " + virtualEnvironment.Trim();
        string needed = string.IsNullOrWhiteSpace(feature)
            ? string.Empty
            : " It is needed for " + feature.Trim() + ".";
        return "The Python module '" + module + "' in the CPython at " + library + " (" + environment
               + ") is " + installed + ", and at least " + minimum
               + " is required because earlier releases carry a known security hole. Upgrade it there with: "
               + UpgradeCommand(module, minimumVersion, virtualEnvironment) + needed;
    }
}

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What <see cref="PythonSupport.Check"/> found out about one Python module it was asked to look for.
/// </summary>
public sealed class PythonModuleReport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PythonModuleReport"/> class for a module with no
    /// version floor.
    /// </summary>
    /// <param name="name">The module name, as the caller asked for it.</param>
    /// <param name="isInstalled">Whether importing it succeeded.</param>
    /// <param name="error">What the import said when it failed; <see langword="null"/> when it worked.</param>
    internal PythonModuleReport(string name, bool isInstalled, string error)
        : this(name, isInstalled, error, null, null, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PythonModuleReport"/> class.
    /// </summary>
    /// <param name="name">The module name, as the caller asked for it.</param>
    /// <param name="isInstalled">Whether importing it succeeded.</param>
    /// <param name="error">What the import said when it failed; <see langword="null"/> when it worked.</param>
    /// <param name="installedVersion">The version the module reports, or <see langword="null"/>.</param>
    /// <param name="minimumVersion">The lowest version this library runs, or <see langword="null"/> for none.</param>
    /// <param name="meetsMinimumVersion">Whether the installed version is at least that.</param>
    internal PythonModuleReport(
        string name,
        bool isInstalled,
        string error,
        string installedVersion,
        string minimumVersion,
        bool meetsMinimumVersion)
    {
        Name = name;
        IsInstalled = isInstalled;
        Error = error;
        InstalledVersion = installedVersion;
        MinimumVersion = minimumVersion;
        MeetsMinimumVersion = meetsMinimumVersion;
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

    /// <summary>
    /// The version the imported module reports about itself, exactly as it reports it - such as
    /// <c>2.6.0+cpu</c> - or <see langword="null"/> when it was not imported or reports none.
    /// </summary>
    public string InstalledVersion { get; }

    /// <summary>
    /// The lowest version of this module this library will run, such as <c>2.6</c>, or
    /// <see langword="null"/> when the module has no floor. A floor exists only where earlier releases
    /// carry a known hole that a hostile model can reach.
    /// </summary>
    public string MinimumVersion { get; }

    /// <summary>
    /// Whether <see cref="InstalledVersion"/> is at least <see cref="MinimumVersion"/>. It is
    /// <see langword="true"/> when the module has no floor or was not imported, and
    /// <see langword="false"/> when the installed version is earlier, is a pre-release of the floor
    /// release, or could not be read.
    /// </summary>
    public bool MeetsMinimumVersion { get; }
}

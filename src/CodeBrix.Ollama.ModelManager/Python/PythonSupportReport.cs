using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What <see cref="PythonSupport.Check"/> found. It is a description, never a verdict that throws: a
/// machine with no CPython at all produces a report with <see cref="IsUsable"/> <see langword="false"/>
/// and a <see cref="Problems"/> list saying why, and nothing is raised.
/// </summary>
public sealed class PythonSupportReport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PythonSupportReport"/> class.
    /// </summary>
    /// <param name="libraryPath">The CPython shared library that was resolved, or <see langword="null"/>.</param>
    /// <param name="librarySource">Where that path came from.</param>
    /// <param name="libraryLoads">Whether the operating system could load the library.</param>
    /// <param name="version">The CPython version, or <see langword="null"/> when it is not known.</param>
    /// <param name="isSupportedVersion">Whether that version is inside the supported range.</param>
    /// <param name="virtualEnvironment">The virtual environment in effect, or <see langword="null"/>.</param>
    /// <param name="virtualEnvironmentSource">Where that environment came from.</param>
    /// <param name="isInitialized">Whether an interpreter is running in this process.</param>
    /// <param name="owner">Which side owns that interpreter.</param>
    /// <param name="modules">One entry per module the caller asked about.</param>
    /// <param name="problems">Human sentences describing everything that is wrong; empty when nothing is.</param>
    internal PythonSupportReport(
        string libraryPath,
        PythonLibrarySource librarySource,
        bool libraryLoads,
        Version version,
        bool isSupportedVersion,
        string virtualEnvironment,
        PythonVirtualEnvironmentSource virtualEnvironmentSource,
        bool isInitialized,
        PythonEngineOwner owner,
        IReadOnlyList<PythonModuleReport> modules,
        IReadOnlyList<string> problems)
    {
        LibraryPath = libraryPath;
        LibrarySource = librarySource;
        LibraryLoads = libraryLoads;
        Version = version;
        IsSupportedVersion = isSupportedVersion;
        VirtualEnvironment = virtualEnvironment;
        VirtualEnvironmentSource = virtualEnvironmentSource;
        IsInitialized = isInitialized;
        Owner = owner;
        Modules = modules ?? Array.Empty<PythonModuleReport>();
        Problems = problems ?? Array.Empty<string>();
    }

    /// <summary>
    /// The full path of the CPython shared library that was resolved, or <see langword="null"/> when none
    /// was found. Once an interpreter is running this is the library it actually loaded.
    /// </summary>
    public string LibraryPath { get; }

    /// <summary>
    /// Where <see cref="LibraryPath"/> came from, as THIS library resolved it. A host application that
    /// started the interpreter itself and named its library in code is reported as
    /// <see cref="PythonLibrarySource.Host"/>: the path is the one the running interpreter reports about
    /// itself, because nothing this library reads names it. <see cref="Owner"/> says the same thing from
    /// the other side.
    /// </summary>
    public PythonLibrarySource LibrarySource { get; }

    /// <summary>
    /// Whether the operating system could load <see cref="LibraryPath"/>. The library is loaded and freed
    /// again to find out; no interpreter is started for it.
    /// </summary>
    public bool LibraryLoads { get; }

    /// <summary>
    /// The CPython version, or <see langword="null"/> when it could not be determined.
    /// </summary>
    public Version Version { get; }

    /// <summary>
    /// Whether <see cref="Version"/> is inside the range the embedding layer supports.
    /// </summary>
    public bool IsSupportedVersion { get; }

    /// <summary>
    /// The virtual environment the interpreter runs out of, or <see langword="null"/> when there is none.
    /// </summary>
    public string VirtualEnvironment { get; }

    /// <summary>
    /// Where <see cref="VirtualEnvironment"/> came from.
    /// </summary>
    public PythonVirtualEnvironmentSource VirtualEnvironmentSource { get; }

    /// <summary>
    /// Whether an interpreter is running in this process at the moment the report was made.
    /// </summary>
    public bool IsInitialized { get; }

    /// <summary>
    /// Which side owns that interpreter.
    /// </summary>
    public PythonEngineOwner Owner { get; }

    /// <summary>
    /// One entry per module the caller asked about, in the order asked. Empty when none were asked for,
    /// which is also the case in which no interpreter was started.
    /// </summary>
    public IReadOnlyList<PythonModuleReport> Modules { get; }

    /// <summary>
    /// Human sentences describing everything that is wrong, ready to be shown or logged. Empty when
    /// nothing is wrong.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>
    /// Whether a Python feature can run: the library loads, its version is supported, and every module
    /// that was asked about imports.
    /// </summary>
    public bool IsUsable => LibraryLoads && IsSupportedVersion && Modules.All(module => module.IsInstalled);
}

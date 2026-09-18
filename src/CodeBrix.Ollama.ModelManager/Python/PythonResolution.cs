using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The answer to "which CPython, out of which virtual environment, and who said so" - worked out from
/// <see cref="PythonOptions"/> and the process environment alone, before any interpreter exists.
/// </summary>
internal sealed class PythonResolution
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PythonResolution"/> class.
    /// </summary>
    /// <param name="libraryPath">The CPython shared library, or <see langword="null"/>.</param>
    /// <param name="librarySource">Where that path came from.</param>
    /// <param name="virtualEnvironment">The virtual environment, or <see langword="null"/>.</param>
    /// <param name="virtualEnvironmentSource">Where that environment came from.</param>
    /// <param name="problems">Sentences describing whatever was named but is not there.</param>
    internal PythonResolution(
        string libraryPath,
        PythonLibrarySource librarySource,
        string virtualEnvironment,
        PythonVirtualEnvironmentSource virtualEnvironmentSource,
        IReadOnlyList<string> problems)
    {
        LibraryPath = libraryPath;
        LibrarySource = librarySource;
        VirtualEnvironment = virtualEnvironment;
        VirtualEnvironmentSource = virtualEnvironmentSource;
        Problems = problems ?? Array.Empty<string>();
    }

    /// <summary>The CPython shared library that was resolved, or <see langword="null"/>.</summary>
    internal string LibraryPath { get; }

    /// <summary>Where <see cref="LibraryPath"/> came from.</summary>
    internal PythonLibrarySource LibrarySource { get; }

    /// <summary>The virtual environment that was resolved, or <see langword="null"/>.</summary>
    internal string VirtualEnvironment { get; }

    /// <summary>Where <see cref="VirtualEnvironment"/> came from.</summary>
    internal PythonVirtualEnvironmentSource VirtualEnvironmentSource { get; }

    /// <summary>Sentences describing whatever was named but is not there. Empty when all is well.</summary>
    internal IReadOnlyList<string> Problems { get; }
}

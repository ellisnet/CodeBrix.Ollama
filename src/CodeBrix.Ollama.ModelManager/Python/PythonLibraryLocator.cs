using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// Works out which CPython shared library and which virtual environment a Python feature would use, and
/// answers two questions about the library that need no interpreter: does the operating system load it,
/// and what version is it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here touches the embedding layer, so a report can be produced on a machine that has no CPython
/// at all without anything native being loaded. The probe order for a library belonging to a virtual
/// environment's base interpreter is the same one the embedding layer uses, so the path reported before
/// an interpreter exists is the path that would be loaded if one were started.
/// </para>
/// <para>
/// Like the store's hard-link call, nothing here throws: a path that is not there, a library the loader
/// refuses and a file whose name says nothing about its version are all answers, not failures.
/// </para>
/// </remarks>
internal static class PythonLibraryLocator
{
    /// <summary>The configuration file every virtual environment carries at its root.</summary>
    internal const string ConfigurationFileName = "pyvenv.cfg";

    /// <summary>The environment variable the embedding layer reads for a virtual environment, preferred.</summary>
    internal const string InheritedVirtualEnvironmentVariable = "PYTHONNET_VENV";

    /// <summary>The environment variable an activated virtual environment leaves in the process.</summary>
    internal const string ActivatedVirtualEnvironmentVariable = "VIRTUAL_ENV";

    /// <summary>The C function every CPython shared library exports, giving its version as text.</summary>
    private const string VersionExportName = "Py_GetVersion";

    /// <summary>
    /// Resolves the library and the virtual environment from options and the process environment, in the
    /// documented order: code, then this library's own environment variable, then whatever the process
    /// already carries.
    /// </summary>
    /// <param name="options">The caller's options; <see langword="null"/> is treated as all-defaults.</param>
    /// <returns>What was resolved, with a sentence for anything that was named but is not there.</returns>
    internal static PythonResolution Resolve(PythonOptions options)
    {
        var problems = new List<string>();

        string virtualEnvironment = null;
        PythonVirtualEnvironmentSource environmentSource = PythonVirtualEnvironmentSource.None;

        if (options != null && !string.IsNullOrWhiteSpace(options.VirtualEnvironment))
        {
            virtualEnvironment = Normalize(options.VirtualEnvironment);
            environmentSource = PythonVirtualEnvironmentSource.Code;
        }
        else if (TryReadVariable(PythonOptions.VirtualEnvironmentVariable, out string fromOurVariable))
        {
            virtualEnvironment = Normalize(fromOurVariable);
            environmentSource = PythonVirtualEnvironmentSource.EnvironmentVariable;
        }
        else if (TryReadVariable(InheritedVirtualEnvironmentVariable, out string fromPreferred))
        {
            virtualEnvironment = Normalize(fromPreferred);
            environmentSource = PythonVirtualEnvironmentSource.Inherited;
        }
        else if (TryReadVariable(ActivatedVirtualEnvironmentVariable, out string fromActivated))
        {
            virtualEnvironment = Normalize(fromActivated);
            environmentSource = PythonVirtualEnvironmentSource.Inherited;
        }

        string configuration = null;
        if (virtualEnvironment != null)
        {
            if (!Directory.Exists(virtualEnvironment))
            {
                problems.Add("The virtual environment " + virtualEnvironment + " does not exist.");
            }
            else
            {
                configuration = Path.Combine(virtualEnvironment, ConfigurationFileName);
                if (!File.Exists(configuration))
                {
                    problems.Add("The folder " + virtualEnvironment + " is not a virtual environment: it has no "
                                 + ConfigurationFileName + " file.");
                    configuration = null;
                }
            }
        }

        string libraryPath = null;
        PythonLibrarySource librarySource = PythonLibrarySource.NotFound;

        if (options != null && !string.IsNullOrWhiteSpace(options.LibraryPath))
        {
            libraryPath = options.LibraryPath.Trim();
            librarySource = PythonLibrarySource.Code;
            if (!File.Exists(libraryPath))
            {
                problems.Add("The CPython shared library named by PythonOptions.LibraryPath does not exist: "
                             + libraryPath + ".");
            }
        }
        else if (TryReadVariable(PythonOptions.LibraryPathVariable, out string fromVariable))
        {
            libraryPath = fromVariable.Trim();
            librarySource = PythonLibrarySource.EnvironmentVariable;
            if (!File.Exists(libraryPath))
            {
                problems.Add("The CPython shared library named by the " + PythonOptions.LibraryPathVariable
                             + " environment variable does not exist: " + libraryPath + ".");
            }
        }
        else if (configuration != null)
        {
            libraryPath = FindLibraryForConfiguration(configuration);
            if (libraryPath == null)
            {
                problems.Add("The base interpreter of the virtual environment " + virtualEnvironment
                             + " ships no CPython shared library that can be embedded. Name one with"
                             + " PythonOptions.LibraryPath or the " + PythonOptions.LibraryPathVariable
                             + " environment variable.");
            }
            else
            {
                librarySource = PythonLibrarySource.VirtualEnvironment;
            }
        }

        return new PythonResolution(libraryPath, librarySource, virtualEnvironment, environmentSource, problems);
    }

    /// <summary>
    /// Loads the library and frees it again, which is the only way to find out whether the operating
    /// system will have it. No interpreter is started and no Python code runs.
    /// </summary>
    /// <param name="path">The library to try, or <see langword="null"/>.</param>
    /// <param name="error">What went wrong, in a phrase; <see langword="null"/> when it loaded.</param>
    /// <returns><see langword="true"/> when the library loaded.</returns>
    internal static bool TryLoad(string path, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "no library was named";
            return false;
        }

        if (!File.Exists(path))
        {
            error = "the file does not exist";
            return false;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            if (!NativeLibrary.TryLoad(path, out handle))
            {
                error = "the operating system refused to load it";
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                try
                {
                    NativeLibrary.Free(handle);
                }
                catch (Exception)
                {
                    //A library that will not unload has still answered the question that was asked.
                }
            }
        }
    }

    /// <summary>
    /// Reads the version of a CPython shared library: from its own <c>Py_GetVersion</c> export where that
    /// can be called, and from the file name otherwise. Neither route starts an interpreter.
    /// </summary>
    /// <param name="path">The library to read, or <see langword="null"/>.</param>
    /// <returns>The version, or <see langword="null"/> when it could not be determined.</returns>
    internal static Version ReadVersion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        Version fromExport = TryReadVersionFromExport(path);
        return fromExport ?? ParseVersionFromFileName(path);
    }

    /// <summary>
    /// Reads the version out of a library file name - <c>libpython3.13.so</c>, <c>python313.dll</c>,
    /// <c>libpython3.13t.so</c>, <c>libpython3.13.dylib</c> - which is the fallback when the library
    /// cannot be called.
    /// </summary>
    /// <param name="path">The library path or file name.</param>
    /// <returns>The major.minor version, or <see langword="null"/> when the name says nothing.</returns>
    internal static Version ParseVersionFromFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string name = Path.GetFileName(path.Trim());
        int marker = name.IndexOf("python", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        string rest = name.Substring(marker + "python".Length);
        int index = 0;
        string major = ReadDigits(rest, ref index);
        if (major.Length == 0)
        {
            return null;
        }

        if (index < rest.Length && rest[index] == '.')
        {
            int afterSeparator = index + 1;
            string minor = ReadDigits(rest, ref afterSeparator);
            if (minor.Length > 0)
            {
                return MakeVersion(major, minor);
            }
        }

        //Windows packs the pair with no separator at all: python313.dll is 3.13, and the dot that
        //follows belongs to the extension, not to the version.
        return major.Length < 2 ? null : MakeVersion(major.Substring(0, 1), major.Substring(1));
    }

    /// <summary>
    /// Reads the interpreter version out of a <c>pyvenv.cfg</c> value. The <c>version</c> key written by
    /// CPython's own venv module is a plain "3.13.5"; the <c>version_info</c> key written by the
    /// virtualenv tool is "3.13.5.final.0", which <see cref="Version"/> rejects outright. Only the
    /// leading numeric parts are kept.
    /// </summary>
    /// <param name="value">The raw value of the configuration key.</param>
    /// <returns>The version, or <see langword="null"/> when the value carries no major.minor pair.</returns>
    internal static Version ParseConfiguredVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Version.TryParse(value, out Version parsed))
        {
            return parsed;
        }

        var numbers = new List<int>();
        foreach (string part in value.Split('.'))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                break;
            }
            numbers.Add(number);
            if (numbers.Count == 3)
            {
                break;
            }
        }

        return numbers.Count >= 2
            ? new Version(numbers[0], numbers[1], numbers.Count > 2 ? numbers[2] : 0)
            : null;
    }

    /// <summary>
    /// Reads the simple <c>key = value</c> lines of a <c>pyvenv.cfg</c>.
    /// </summary>
    /// <param name="configurationPath">The full path of the file.</param>
    /// <returns>The settings, or an empty set when the file cannot be read.</returns>
    internal static IReadOnlyDictionary<string, string> ReadConfiguration(string configurationPath)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string line in File.ReadAllLines(configurationPath))
            {
                string[] parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    settings[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }
        catch (IOException)
        {
            //An unreadable configuration file means no library from it, which is already an answer.
        }
        catch (UnauthorizedAccessException)
        {
            //As above.
        }

        return settings;
    }

    /// <summary>
    /// Finds the CPython shared library belonging to the base interpreter a <c>pyvenv.cfg</c> records.
    /// </summary>
    /// <param name="configurationPath">The full path of the environment's <c>pyvenv.cfg</c>.</param>
    /// <returns>The library path, or <see langword="null"/> when none was found.</returns>
    internal static string FindLibraryForConfiguration(string configurationPath)
    {
        IReadOnlyDictionary<string, string> settings = ReadConfiguration(configurationPath);
        if (!settings.TryGetValue("home", out string home) || string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        Version version = null;
        if (settings.TryGetValue("version", out string plain))
        {
            version = ParseConfiguredVersion(plain);
        }
        if (version == null && settings.TryGetValue("version_info", out string detailed))
        {
            version = ParseConfiguredVersion(detailed);
        }
        if (version == null)
        {
            return null;
        }

        //Both builds are probed: pyvenv.cfg does not record whether the base interpreter is free-threaded.
        string[] names = { GetLibraryFileName(version, false), GetLibraryFileName(version, true) };

        foreach (string directory in GetLibrarySearchDirectories())
        {
            foreach (string name in names)
            {
                string candidate = Path.Combine(home.Trim(), directory, name);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The folders, relative to the base interpreter's <c>home</c>, a belonging library is looked for in,
    /// in probe order. This mirrors the embedding layer's own order, so that what is reported before an
    /// interpreter exists is what would be loaded.
    /// </summary>
    /// <returns>Relative folder paths for the running operating system and process architecture.</returns>
    internal static IReadOnlyList<string> GetLibrarySearchDirectories()
    {
        var directories = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            //The library sits next to python.exe.
            directories.Add(".");
            return directories;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Architecture architecture = RuntimeInformation.ProcessArchitecture;
            if (architecture == Architecture.X64 || architecture == Architecture.Arm64)
            {
                directories.Add("../lib64");
            }
            directories.Add("../lib");
            foreach (string tuple in GetLinuxMultiarchTuples(architecture))
            {
                directories.Add("../lib/" + tuple);
            }
            return directories;
        }

        //macOS framework builds record a home of <framework>/Versions/<X.Y>/bin.
        directories.Add("../lib");
        return directories;
    }

    /// <summary>
    /// The GNU architecture tuples naming the multiarch library folder of the distributions - Debian,
    /// Ubuntu and their derivatives - that keep libpython nowhere else.
    /// </summary>
    /// <param name="architecture">The architecture the current process runs as.</param>
    /// <returns>Folder names such as <c>x86_64-linux-gnu</c>; empty where none is known.</returns>
    internal static IReadOnlyList<string> GetLinuxMultiarchTuples(Architecture architecture)
    {
        switch (architecture)
        {
            case Architecture.X64:
                return new[] { "x86_64-linux-gnu" };
            case Architecture.X86:
                return new[] { "i386-linux-gnu" };
            case Architecture.Arm64:
                return new[] { "aarch64-linux-gnu" };
            case Architecture.Arm:
            case Architecture.Armv6:
                return new[] { "arm-linux-gnueabihf", "arm-linux-gnueabi" };
            case Architecture.RiscV64:
                return new[] { "riscv64-linux-gnu" };
            case Architecture.Ppc64le:
                return new[] { "powerpc64le-linux-gnu" };
            case Architecture.S390x:
                return new[] { "s390x-linux-gnu" };
            case Architecture.LoongArch64:
                return new[] { "loongarch64-linux-gnu" };
            default:
                return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The file name a CPython shared library of a given version carries on the running platform.
    /// </summary>
    /// <param name="version">The interpreter version; only major and minor are used.</param>
    /// <param name="freeThreaded">Whether to name the free-threaded build.</param>
    /// <returns>A file name such as <c>libpython3.13.so</c> or <c>python313.dll</c>.</returns>
    internal static string GetLibraryFileName(Version version, bool freeThreaded)
    {
        if (version == null)
        {
            return null;
        }

        string threading = freeThreaded ? "t" : string.Empty;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return string.Format(CultureInfo.InvariantCulture, "python{0}{1}{2}.dll",
                version.Major, version.Minor, threading);
        }

        string extension = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";
        return string.Format(CultureInfo.InvariantCulture, "libpython{0}.{1}{2}{3}",
            version.Major, version.Minor, threading, extension);
    }

    /// <summary>
    /// Calls the library's own <c>Py_GetVersion</c>, which returns a static string and needs no running
    /// interpreter, and parses the version out of what it says.
    /// </summary>
    /// <param name="path">The library to ask.</param>
    /// <returns>The version, or <see langword="null"/> when the library cannot be asked.</returns>
    private static unsafe Version TryReadVersionFromExport(string path)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            if (!NativeLibrary.TryLoad(path, out handle))
            {
                return null;
            }

            if (!NativeLibrary.TryGetExport(handle, VersionExportName, out IntPtr export) || export == IntPtr.Zero)
            {
                return null;
            }

            delegate* unmanaged[Cdecl]<byte*> getVersion = (delegate* unmanaged[Cdecl]<byte*>)export;
            string text = Marshal.PtrToStringUTF8((IntPtr)getVersion());
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            //"3.13.5 (main, Aug 10 2026, 12:06:59) [GCC 14.2.0]" - only the leading token is a version.
            string leading = text.Trim().Split(' ')[0];
            return ParseConfiguredVersion(leading);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                try
                {
                    NativeLibrary.Free(handle);
                }
                catch (Exception)
                {
                    //As in TryLoad: an unload that fails has not changed the answer.
                }
            }
        }
    }

    /// <summary>
    /// Reads an environment variable, treating blank as absent.
    /// </summary>
    /// <param name="name">The variable to read.</param>
    /// <param name="value">What it holds, when it holds anything.</param>
    /// <returns><see langword="true"/> when the variable is set to something that is not blank.</returns>
    private static bool TryReadVariable(string name, out string value)
    {
        value = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// Trims a folder path and removes a trailing separator, so that what is reported matches what an
    /// interpreter would report as its prefix.
    /// </summary>
    /// <param name="value">The raw path.</param>
    /// <returns>The tidied path.</returns>
    private static string Normalize(string value) => Path.TrimEndingDirectorySeparator(value.Trim());

    /// <summary>
    /// Reads the run of decimal digits starting at a position.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="index">Where to start; left just past the digits.</param>
    /// <returns>The digits, possibly empty.</returns>
    private static string ReadDigits(string text, ref int index)
    {
        var digits = new StringBuilder();
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            digits.Append(text[index]);
            index++;
        }
        return digits.ToString();
    }

    /// <summary>
    /// Builds a major.minor version from two digit runs.
    /// </summary>
    /// <param name="major">The major digits.</param>
    /// <param name="minor">The minor digits.</param>
    /// <returns>The version, or <see langword="null"/> when either run is not a number.</returns>
    private static Version MakeVersion(string major, string minor)
    {
        if (int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out int majorNumber)
            && int.TryParse(minor, NumberStyles.None, CultureInfo.InvariantCulture, out int minorNumber))
        {
            return new Version(majorNumber, minorNumber);
        }
        return null;
    }
}

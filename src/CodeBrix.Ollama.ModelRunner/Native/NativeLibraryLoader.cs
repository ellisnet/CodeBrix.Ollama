using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: CodeBrix.VideoPlayback.Dav1d Interop/Dav1dLibrary.cs;

/// <summary>
/// Finds and loads the native inference library, and checks that the one it found is the build this binding
/// was written against.
/// </summary>
/// <remarks>
/// <para>
/// The package ships seven natives in the standard NuGet <c>runtimes/&lt;rid&gt;/native/</c> layout. When an
/// application publishes for one runtime identifier, the build system copies the right one beside the
/// application and the operating system finds it without help. When an application publishes without a
/// runtime identifier - which is the ordinary case for a library test run, and common for desktop
/// applications - the natives stay in their <c>runtimes/&lt;rid&gt;/native/</c> folders and nothing looks
/// there. The resolver installed here does.
/// </para>
/// <para>
/// If nothing can be loaded, the exception lists every path that was tried. A missing native is nearly
/// always a packaging or publishing question - the wrong runtime identifier, a trimmed output folder, a
/// single-file bundle that did not extract - and the list of paths is what answers it.
/// </para>
/// <para>
/// Once something has loaded, its own identity is checked before anything else calls into it: the library
/// reports the upstream commit it was built from and the runtime identifier it was built for, and both have
/// to agree with what this binding expects. A library that answers to the name but is a different build
/// would read the parameter structures with a different layout, which is a fault worth catching at the door.
/// </para>
/// </remarks>
internal static class NativeLibraryLoader
{
    /// <summary>The name every <c>LibraryImport</c> declaration in this assembly asks for.</summary>
    public const string LibraryName = "codebrix_llama";

    /// <summary>
    /// The short upstream llama.cpp commit the vendored headers, and therefore these structure layouts,
    /// were taken from. The full commit is 815a2a5915f22ce6a760c676389c5dfe8535c08f.
    /// </summary>
    public const string ExpectedUpstreamCommit = "815a2a59";

    private static readonly object Gate = new object();
    private static readonly object InitializationGate = new object();

    private static int initializationCount;
    private static IntPtr handle;
    private static string loadedPath;
    private static bool resolverInstalled;
    private static bool checkedAndInitialized;
    private static string buildInfo;
    private static string reportedRuntimeIdentifier;

    /// <summary>The full path of the native library that was loaded, once one has been.</summary>
    /// <remarks>
    /// Reads "codebrix_llama (operating-system search path)" when the library was found by the platform
    /// loader rather than beside the assembly, because in that case there is no path this code ever saw.
    /// </remarks>
    public static string LoadedPath
    {
        get
        {
            lock (Gate) return loadedPath;
        }
    }

    /// <summary>The build record the loaded library reports: upstream commit and tag, build date and host.</summary>
    public static string BuildInfo
    {
        get
        {
            EnsureLoaded();
            lock (Gate) return buildInfo;
        }
    }

    /// <summary>The runtime identifier the loaded library reports it was built for.</summary>
    public static string ReportedRuntimeIdentifier
    {
        get
        {
            EnsureLoaded();
            lock (Gate) return reportedRuntimeIdentifier;
        }
    }

    /// <summary>True once the native library has been loaded, checked and initialized.</summary>
    public static bool IsLoaded
    {
        get
        {
            lock (Gate) return handle != IntPtr.Zero && checkedAndInitialized;
        }
    }

    /// <summary>How many times the identity check and backend start have run; one, once anything has loaded.</summary>
    /// <remarks>
    /// Written once under the initialization gate. A test reads it to prove that threads arriving together
    /// do the work between them rather than each doing all of it.
    /// </remarks>
    internal static int InitializationCount
    {
        get
        {
            lock (Gate) return initializationCount;
        }
    }

    /// <summary>The runtime identifier folder this platform's native library lives in.</summary>
    /// <exception cref="NativeLibraryException">This package ships no native for the current platform.</exception>
    public static string RuntimeIdentifier
    {
        get
        {
            string os = OperatingSystemMoniker();
            string architecture = ArchitectureMoniker();

            if (os == null || architecture == null)
            {
                throw new NativeLibraryException(
                    "CodeBrix.Ollama.ModelRunner ships native inference libraries for Windows (x64, ARM64), "
                    + "macOS (x64, ARM64) and Linux (x64, ARM64, RISC-V 64); this process is running on "
                    + $"{RuntimeInformation.OSDescription} / {RuntimeInformation.ProcessArchitecture}, which is "
                    + "none of them.");
            }

            return os + "-" + architecture;
        }
    }

    /// <summary>The file name the native library has on this platform.</summary>
    public static string NativeFileName
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return LibraryName + ".dll";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "lib" + LibraryName + ".dylib";
            return "lib" + LibraryName + ".so";
        }
    }

    /// <summary>
    /// Loads the native library if it is not loaded already, checks that it is the expected build, and
    /// initializes the engine's backends once.
    /// </summary>
    /// <exception cref="NativeLibraryException">
    /// The library could not be found - the message lists every path that was tried - or it reports an
    /// upstream commit or a runtime identifier this binding was not written against.
    /// </exception>
    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (handle != IntPtr.Zero && checkedAndInitialized) return;
        }

        // The identity check and the backend start run under their own gate rather than under Gate, because
        // they call into the native library and the import resolver takes Gate while they do. One gate for
        // the state, another for the sequence: two threads arriving together, which is the ordinary case
        // when an application loads two models at once, do the work once between them.
        lock (InitializationGate)
        {
            lock (Gate)
            {
                if (handle != IntPtr.Zero && checkedAndInitialized) return;

                InstallResolverNoLock();

                if (handle == IntPtr.Zero && LoadNoLock() == IntPtr.Zero)
                {
                    throw new NativeLibraryException(
                        DescribeLoadFailure(BaseDirectories(), RuntimeIdentifier, NativeFileName));
                }
            }

            // Outside Gate: these call into the native library, which loads through the resolver above.
            // The log callback goes in first, so the lines the backends write while starting are captured too.
            NativeLog.Install();

            string reportedBuild = ProbeIdentity("codebrix_llama_build_info", ReadReportedBuildInfo);
            string reportedRid = ProbeIdentity("codebrix_llama_rid", ReadReportedRuntimeIdentifier);
            string path = LoadedPath;

            if (reportedBuild == null
                || reportedBuild.IndexOf(ExpectedUpstreamCommit, StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new NativeLibraryException(
                    $"The native inference library at '{path}' reports the build record "
                    + $"'{reportedBuild ?? "(none)"}', which does not name upstream llama.cpp commit "
                    + $"{ExpectedUpstreamCommit}. This binding's parameter structures were written from the "
                    + "headers of that commit and would be read with the wrong layout by any other build. "
                    + "Replace the native library with one built by this repository's llama-native-tools, or "
                    + "use a version of this package built for the library you have.");
            }

            string expectedRid = RuntimeIdentifier;
            if (!string.Equals(reportedRid, expectedRid, StringComparison.Ordinal))
            {
                throw new NativeLibraryException(
                    $"The native inference library at '{path}' reports that it was built for runtime identifier "
                    + $"'{reportedRid ?? "(none)"}', but this process is '{expectedRid}'. The wrong native was "
                    + "copied into the output folder; check the runtimes/ layout of the application's publish.");
            }

            NativeMethods.llama_backend_init();

            lock (Gate)
            {
                buildInfo = reportedBuild;
                reportedRuntimeIdentifier = reportedRid;
                checkedAndInitialized = true;
                initializationCount++;
            }
        }
    }

    /// <summary>Builds the "nothing could be loaded" message, listing every path that would be tried.</summary>
    /// <param name="baseDirectories">The directories to probe, in order.</param>
    /// <param name="runtimeIdentifier">The runtime identifier folder to look under.</param>
    /// <param name="fileName">The native file name to look for.</param>
    /// <returns>The message.</returns>
    /// <remarks>Separated out so a test can read it without having to hide the real library first.</remarks>
    internal static string DescribeLoadFailure(
        IReadOnlyList<string> baseDirectories,
        string runtimeIdentifier,
        string fileName)
    {
        StringBuilder message = new StringBuilder();
        message.Append("The native inference library could not be loaded. ");
        message.Append($"CodeBrix.Ollama.ModelRunner looked for '{fileName}' at:");

        foreach (string candidate in EnumerateProbePaths(baseDirectories, runtimeIdentifier, fileName))
        {
            message.Append(Environment.NewLine);
            message.Append("    ");
            message.Append(candidate);
        }

        message.Append(Environment.NewLine);
        message.Append("    ");
        message.Append(fileName);
        message.Append("  (the operating system's own search path)");
        message.Append(Environment.NewLine);
        message.Append(
            "The package ships this library under runtimes/" + runtimeIdentifier + "/native/. If it is missing, "
            + "the application was probably published in a way that dropped it - a trimmed or single-file "
            + "publish, a manual copy of the managed assembly alone, or a runtime identifier the package has no "
            + "native for.");
        return message.ToString();
    }

    /// <summary>Lists, in order, every path the loader would try.</summary>
    /// <param name="baseDirectories">The directories to probe.</param>
    /// <param name="runtimeIdentifier">The runtime identifier folder to look under.</param>
    /// <param name="fileName">The native file name to look for.</param>
    /// <returns>The candidate paths.</returns>
    /// <remarks>
    /// The <c>runtimes/&lt;rid&gt;/native/</c> path comes first in each directory, because that is the layout
    /// this repository stages into every output folder and the one the package ships; a loose copy beside the
    /// assembly is the fallback, not the rule.
    /// </remarks>
    internal static IReadOnlyList<string> EnumerateProbePaths(
        IReadOnlyList<string> baseDirectories,
        string runtimeIdentifier,
        string fileName)
    {
        List<string> paths = new List<string>();

        foreach (string directory in baseDirectories)
        {
            if (string.IsNullOrEmpty(directory)) continue;
            Add(paths, Path.Combine(directory, "runtimes", runtimeIdentifier, "native", fileName));
            Add(paths, Path.Combine(directory, fileName));
        }

        return paths;
    }

    /// <summary>The directories the loader probes, most likely first.</summary>
    /// <returns>The directories, without duplicates.</returns>
    internal static IReadOnlyList<string> BaseDirectories()
    {
        List<string> directories = new List<string>();
        Add(directories, AppContext.BaseDirectory);

        string assemblyDirectory = null;
        try
        {
            string location = typeof(NativeLibraryLoader).Assembly.Location;
            if (!string.IsNullOrEmpty(location)) assemblyDirectory = Path.GetDirectoryName(location);
        }
        catch (NotSupportedException)
        {
            // A single-file or in-memory assembly has no location; the base directory is all there is.
        }

        Add(directories, assemblyDirectory);
        return directories;
    }

    /// <summary>Installs the resolver, without loading anything.</summary>
    /// <remarks>
    /// Called from the static constructor of the type that carries the imports, so the resolver is in place
    /// before any of them can run - including when something calls an import without going through
    /// <see cref="EnsureLoaded"/> first.
    /// </remarks>
    public static void EnsureResolverInstalled()
    {
        lock (Gate) InstallResolverNoLock();
    }

    /// <summary>Reads a NUL-terminated UTF-8 string the native library owns.</summary>
    /// <param name="text">The pointer, which may be null.</param>
    /// <returns>The text, or <see langword="null"/> when the pointer was null.</returns>
    internal static unsafe string ReadUtf8(byte* text)
    {
        return text == null ? null : Marshal.PtrToStringUTF8((IntPtr)text);
    }

    /// <summary>Runs one of the library's identity probes, turning a missing export into a clear failure.</summary>
    /// <param name="entryPoint">The exported name being asked for, which goes into the message.</param>
    /// <param name="read">The probe.</param>
    /// <returns>Whatever the probe returned.</returns>
    /// <exception cref="NativeLibraryException">
    /// The loaded library does not export <paramref name="entryPoint"/>, which means it is not this
    /// repository's build.
    /// </exception>
    /// <remarks>
    /// A stock llama.cpp of the right name and the right shape exports none of the codebrix_ entry points, so
    /// the very first call made through the resolver is the one that fails. Left alone that surfaces as an
    /// <see cref="EntryPointNotFoundException"/> from deep inside a property getter, which says nothing about
    /// what is actually wrong; the point of the identity check is to say it.
    /// </remarks>
    internal static string ProbeIdentity(string entryPoint, Func<string> read)
    {
        try
        {
            return read();
        }
        catch (EntryPointNotFoundException exception)
        {
            throw IdentityProbeFailure(entryPoint, exception);
        }
        catch (DllNotFoundException exception)
        {
            throw IdentityProbeFailure(entryPoint, exception);
        }
    }

    private static NativeLibraryException IdentityProbeFailure(string entryPoint, Exception exception)
    {
        return new NativeLibraryException(
            $"The native library loaded from '{LoadedPath}' does not export '{entryPoint}', so it is not a "
            + "CodeBrix build of the inference engine. This binding only runs against the library built by "
            + "this repository's llama-native-tools, which exports that entry point and reports the upstream "
            + $"commit ({ExpectedUpstreamCommit}) and runtime identifier it was built from. A stock llama.cpp "
            + "shared library of the same name, earlier on the search path, is the usual cause.",
            exception);
    }

    private static unsafe string ReadReportedBuildInfo()
    {
        return ReadUtf8(NativeMethods.codebrix_llama_build_info());
    }

    private static unsafe string ReadReportedRuntimeIdentifier()
    {
        return ReadUtf8(NativeMethods.codebrix_llama_rid());
    }

    private static void InstallResolverNoLock()
    {
        if (resolverInstalled) return;
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, Resolve);
        resolverInstalled = true;
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal)) return IntPtr.Zero;

        lock (Gate)
        {
            if (handle != IntPtr.Zero) return handle;
            return LoadNoLock();
        }
    }

    private static IntPtr LoadNoLock()
    {
        string fileName = NativeFileName;
        string runtimeIdentifier;

        try
        {
            runtimeIdentifier = RuntimeIdentifier;
        }
        catch (NativeLibraryException)
        {
            runtimeIdentifier = null;
        }

        if (runtimeIdentifier != null)
        {
            foreach (string candidate in EnumerateProbePaths(BaseDirectories(), runtimeIdentifier, fileName))
            {
                if (!File.Exists(candidate)) continue;
                if (!System.Runtime.InteropServices.NativeLibrary.TryLoad(candidate, out IntPtr found)) continue;

                handle = found;
                loadedPath = candidate;
                return found;
            }
        }

        if (System.Runtime.InteropServices.NativeLibrary.TryLoad(
                LibraryName, typeof(NativeLibraryLoader).Assembly, null, out IntPtr system))
        {
            handle = system;
            loadedPath = LibraryName + " (operating-system search path)";
            return system;
        }

        return IntPtr.Zero;
    }

    private static string OperatingSystemMoniker()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "osx";
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : null;
    }

    private static string ArchitectureMoniker() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.RiscV64 => "riscv64",
            _ => null,
        };

    private static void Add(List<string> target, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        foreach (string existing in target)
        {
            if (string.Equals(existing, value, StringComparison.Ordinal)) return;
        }

        target.Add(value);
    }
}

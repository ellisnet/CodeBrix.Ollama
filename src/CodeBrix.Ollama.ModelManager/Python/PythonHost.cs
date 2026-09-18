using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Python;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The one place in this library that knows there is an embedding layer at all. Every CodeBrix.Python
/// type named anywhere in CodeBrix.Ollama.ModelManager is named here, which is what makes the dependency
/// inert: the runtime loads that assembly when a method of this class is compiled, and no other code path
/// reaches one, so obtaining, listing, resolving and materializing models never load it.
/// </summary>
/// <remarks>
/// <para>
/// OWNERSHIP. There is one interpreter per process and it cannot be restarted, so this class is its one
/// owner. <see cref="EnsureInitialized"/> runs at most once under a static lock. If an interpreter was
/// ALREADY running when it first looked, the host application embedded Python itself: nothing here
/// configures it and nothing here ends it. Otherwise this library owns it - it applies the options,
/// starts the interpreter, hands the interpreter lock back so that any thread can take it for a run, and
/// sets the bounded process-exit mode as a safety net for a consumer who never disposes anything.
/// </para>
/// <para>
/// SHUTDOWN. <see cref="Shutdown"/> is the one explicit disposal, it happens at most once, and it is a
/// no-op when the host owns the interpreter. Afterwards every entry point that would touch Python refuses
/// to work; the interpreter is never started again.
/// </para>
/// <para>
/// THREADS. After initialization the interpreter lock is released, so a run may happen on any thread as
/// long as it takes the lock for the duration - which every run here does, and only for the duration. No
/// await ever happens inside one.
/// </para>
/// </remarks>
internal static class PythonHost
{
    /// <summary>What every entry point says once Python has been shut down for the process.</summary>
    internal const string ShutDownMessage =
        "Python has been shut down for this process; it cannot be initialized again.";

    /// <summary>Guards initialization and shutdown, which happen at most once each.</summary>
    private static readonly object InitializeLocker = new object();

    /// <summary>Serializes script runs: one run at a time, for the duration of the run only.</summary>
    private static readonly SemaphoreSlim RunLocker = new SemaphoreSlim(1, 1);

    /// <summary>Whether an interpreter this class may use is running.</summary>
    private static bool _isInitialized;

    /// <summary>Whether this class has configured and started an interpreter of its own.</summary>
    private static bool _hasConfigured;

    /// <summary>Whether Python has been shut down for this process.</summary>
    private static bool _isShutDown;

    /// <summary>Who owns the interpreter.</summary>
    private static PythonEngineOwner _owner = PythonEngineOwner.None;

    /// <summary>
    /// Whether an interpreter is running in this process, whoever started it.
    /// </summary>
    internal static bool IsInitialized => PythonEngine.IsInitialized;

    /// <summary>
    /// Which side owns the interpreter. It keeps reporting
    /// <see cref="PythonEngineOwner.ModelManager"/> after <see cref="Shutdown"/>, because that is who
    /// owned the interpreter that was ended.
    /// </summary>
    internal static PythonEngineOwner Owner => _owner;

    /// <summary>Whether Python has been shut down for this process.</summary>
    internal static bool IsShutDown => _isShutDown;

    /// <summary>The lowest CPython version the embedding layer supports.</summary>
    internal static Version MinimumSupportedVersion => PythonEngine.MinSupportedVersion;

    /// <summary>The highest CPython version the embedding layer supports.</summary>
    internal static Version MaximumSupportedVersion => PythonEngine.MaxSupportedVersion;

    /// <summary>
    /// Describes what Python this process would use, and whether each named module imports. It never
    /// throws: everything that is wrong comes back in the report.
    /// </summary>
    /// <param name="options">The caller's options; <see langword="null"/> is treated as all-defaults.</param>
    /// <param name="requiredModules">
    /// The modules to look for. With none, no interpreter is started and none is needed; with any, an
    /// interpreter is started under the ownership rules and each module is imported.
    /// </param>
    /// <returns>The report.</returns>
    internal static PythonSupportReport Check(PythonOptions options, string[] requiredModules)
    {
        string[] modules = CleanModuleNames(requiredModules);
        var problems = new List<string>();

        PythonResolution resolution = PythonLibraryLocator.Resolve(options);
        problems.AddRange(resolution.Problems);

        string libraryPath = resolution.LibraryPath;
        PythonLibrarySource librarySource = resolution.LibrarySource;
        bool loads = false;
        Version version = null;
        bool isSupported = false;
        bool engineRunning = PythonEngine.IsInitialized;

        if (engineRunning)
        {
            //A running interpreter answers for itself: whatever it loaded is what a module import will use.
            loads = true;
            string running = Runtime.PythonDLL;
            if (!string.IsNullOrWhiteSpace(running))
            {
                libraryPath = TidyPath(running);
            }
            librarySource = DescribeRunningLibrarySource(librarySource, libraryPath);
            version = ReadRunningVersion();
        }
        else if (libraryPath == null)
        {
            if (resolution.Problems.Count == 0)
            {
                problems.Add(NoLibraryFoundSentence());
            }
        }
        else
        {
            loads = PythonLibraryLocator.TryLoad(libraryPath, out string loadError);
            if (!loads)
            {
                problems.Add("The CPython shared library at " + libraryPath + " could not be loaded: "
                             + loadError + ".");
            }
            version = PythonLibraryLocator.ReadVersion(libraryPath);
        }

        if (version == null)
        {
            if (libraryPath != null)
            {
                problems.Add("The version of the CPython shared library at " + libraryPath
                             + " could not be determined.");
            }
        }
        else
        {
            isSupported = PythonEngine.IsSupportedVersion(version);
            if (!isSupported)
            {
                problems.Add("CPython " + version.ToString(2) + " at " + (libraryPath ?? "(unknown path)")
                             + " is outside the supported range " + Describe(MinimumSupportedVersion)
                             + " to " + Describe(MaximumSupportedVersion) + ".");
            }
        }

        var moduleReports = new List<PythonModuleReport>();

        if (modules.Length > 0)
        {
            if (_isShutDown)
            {
                problems.Add(ShutDownMessage);
                AddUncheckedModules(moduleReports, modules, ShutDownMessage);
            }
            else if (!loads || !isSupported)
            {
                AddUncheckedModules(moduleReports, modules,
                    "no interpreter could be started, so the module was not looked for");
            }
            else
            {
                try
                {
                    EnsureInitialized(resolution);

                    string running = Runtime.PythonDLL;
                    if (!string.IsNullOrWhiteSpace(running))
                    {
                        libraryPath = TidyPath(running);
                    }

                    RunLocker.Wait();
                    try
                    {
                        Version fromProbe = ReadProbedVersion();
                        if (fromProbe != null)
                        {
                            version = fromProbe;
                            isSupported = PythonEngine.IsSupportedVersion(version);
                        }
                        ImportModules(modules, moduleReports);
                    }
                    finally
                    {
                        RunLocker.Release();
                    }

                    foreach (PythonModuleReport module in moduleReports)
                    {
                        if (!module.IsInstalled)
                        {
                            problems.Add("The Python module '" + module.Name + "' is not installed in the CPython at "
                                         + (libraryPath ?? "(unknown path)") + ": " + module.Error + ".");
                        }
                    }
                }
                catch (Exception exception)
                {
                    problems.Add("Python could not be started: " + exception.Message);
                    AddUncheckedModules(moduleReports, modules, exception.Message);
                }
            }
        }

        return new PythonSupportReport(
            libraryPath,
            librarySource,
            loads,
            version,
            isSupported,
            resolution.VirtualEnvironment,
            resolution.VirtualEnvironmentSource,
            PythonEngine.IsInitialized,
            _owner,
            moduleReports,
            problems);
    }

    /// <summary>
    /// Checks what <see cref="Check"/> checks and throws the friendly exception when a feature cannot run.
    /// </summary>
    /// <param name="options">The caller's options.</param>
    /// <param name="feature">The feature that needs Python, named as a phrase.</param>
    /// <param name="requiredModules">The modules the feature imports.</param>
    /// <exception cref="InvalidOperationException">Python has been shut down for this process.</exception>
    /// <exception cref="PythonNotAvailableException">There is no usable CPython.</exception>
    /// <exception cref="PythonModuleNotInstalledException">A module the feature needs is not installed.</exception>
    internal static void Require(PythonOptions options, string feature, string[] requiredModules)
    {
        ThrowIfShutDown();

        PythonSupportReport report = Check(options, requiredModules);
        if (report.IsUsable)
        {
            return;
        }

        if (!report.LibraryLoads || !report.IsSupportedVersion)
        {
            throw new PythonNotAvailableException(
                feature,
                DescribeLibraryProblem(report),
                MinimumSupportedVersion,
                MaximumSupportedVersion);
        }

        foreach (PythonModuleReport module in report.Modules)
        {
            if (!module.IsInstalled)
            {
                throw new PythonModuleNotInstalledException(
                    feature, module.Name, report.LibraryPath, report.VirtualEnvironment);
            }
        }

        //Usable is the conjunction of the three above, so this cannot be reached; it is here so that a
        //future property added to the report cannot make Require return quietly on a report that failed.
        throw new PythonNotAvailableException(
            feature, DescribeLibraryProblem(report), MinimumSupportedVersion, MaximumSupportedVersion);
    }

    /// <summary>
    /// Runs one of the scripts that ship in this package and returns what it left in its result variable.
    /// One run happens at a time; the interpreter lock is held for the run and nothing is awaited inside it.
    /// </summary>
    /// <param name="options">The caller's options.</param>
    /// <param name="feature">The feature the script is running for, named as a phrase.</param>
    /// <param name="scriptName">The script's file name, such as <c>probe_version.py</c>.</param>
    /// <param name="parameters">Values to set as script variables before it runs, or <see langword="null"/>.</param>
    /// <param name="requiredModules">The modules the script imports, checked before it runs.</param>
    /// <param name="cancellationToken">Cancels the wait for the interpreter, never a run in progress.</param>
    /// <returns>The dictionary the script left in <c>result</c>, converted to .NET values.</returns>
    internal static async Task<IReadOnlyDictionary<string, object>> RunScriptAsync(
        PythonOptions options,
        string feature,
        string scriptName,
        IReadOnlyDictionary<string, object> parameters,
        string[] requiredModules,
        CancellationToken cancellationToken = default)
    {
        ThrowIfShutDown();
        Require(options, feature, requiredModules);
        EnsureInitialized(PythonLibraryLocator.Resolve(options));

        await RunLocker.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfShutDown();
            return RunScript(feature, scriptName, parameters);
        }
        finally
        {
            RunLocker.Release();
        }
    }

    /// <summary>
    /// Ends Python for this process: at most once, and never when the host owns the interpreter.
    /// </summary>
    internal static void Shutdown()
    {
        lock (InitializeLocker)
        {
            if (_isShutDown || _owner != PythonEngineOwner.ModelManager)
            {
                //Nothing to end, or somebody else's interpreter to leave alone.
                return;
            }

            //The flag flips first, so that the interpreter is ended exactly once however the call below
            //turns out, and so that no other thread can start work against an interpreter that is going.
            _isShutDown = true;
            _isInitialized = false;

            ClearPendingError();
            ReleaseHeldObjects();

            PythonEngine.Shutdown();
        }
    }

    /// <summary>
    /// Resets the remembered state. Only the tests call this, and only to check the refusal that follows
    /// a shutdown; it does NOT start an interpreter again, which cannot be done in any case.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (InitializeLocker)
        {
            _isShutDown = false;
            _isInitialized = false;
            _hasConfigured = false;
            _owner = PythonEngineOwner.None;
        }
    }

    /// <summary>
    /// Starts the interpreter if this process has none, exactly once, under the ownership rules.
    /// </summary>
    /// <param name="resolution">What to configure, when this library is the one doing the configuring.</param>
    /// <exception cref="InvalidOperationException">Python has been shut down for this process.</exception>
    private static void EnsureInitialized(PythonResolution resolution)
    {
        ThrowIfShutDown();

        if (_isInitialized)
        {
            return;
        }

        lock (InitializeLocker)
        {
            ThrowIfShutDown();

            if (_isInitialized)
            {
                return;
            }

            if (PythonEngine.IsInitialized && !_hasConfigured)
            {
                //The host embedded Python before this library ever looked. Configure nothing, end nothing.
                _owner = PythonEngineOwner.Host;
                _isInitialized = true;
                return;
            }

            _hasConfigured = true;
            _owner = PythonEngineOwner.ModelManager;

            //The virtual environment first and the library second, and the program name never: reading it
            //before the library is resolvable poisons the process, and assigning it after the environment
            //undoes the activation the environment is there for.
            if (resolution.VirtualEnvironment != null
                && (resolution.VirtualEnvironmentSource == PythonVirtualEnvironmentSource.Code
                    || resolution.VirtualEnvironmentSource == PythonVirtualEnvironmentSource.EnvironmentVariable))
            {
                PythonEngine.VirtualEnvironment = resolution.VirtualEnvironment;
            }

            if (resolution.LibraryPath != null
                && resolution.LibrarySource != PythonLibrarySource.VirtualEnvironment)
            {
                Runtime.PythonDLL = resolution.LibraryPath;
            }

            //The safety net for a consumer who never calls Shutdown: without it, a process that started an
            //interpreter and simply returned from Main would wait at exit for a lock nobody can release.
            PythonEngine.ProcessExitShutdown = ProcessExitShutdownMode.WaitWithTimeout;

            PythonEngine.Initialize();

            //Initialization leaves the interpreter lock held by this thread. Handing it back is what lets a
            //run happen on any thread, each taking the lock for its own duration. The saved state is not
            //kept: shutting down takes the lock again for itself, from whatever thread asks for it.
            _ = PythonEngine.BeginAllowThreads();

            _isInitialized = true;
        }
    }

    /// <summary>
    /// Runs a script with the interpreter lock held, converting what it leaves behind to .NET values.
    /// </summary>
    /// <param name="feature">The feature the script is running for.</param>
    /// <param name="scriptName">The script to run.</param>
    /// <param name="parameters">Values to set as script variables, or <see langword="null"/>.</param>
    /// <returns>The dictionary the script left in its result variable.</returns>
    private static IReadOnlyDictionary<string, object> RunScript(
        string feature, string scriptName, IReadOnlyDictionary<string, object> parameters)
    {
        string text = PythonScripts.Read(scriptName);
        PythonScriptImports.Check(scriptName, text);

        try
        {
            using (Py.GIL())
            {
                using PyModule scope = Py.CreateScope();

                if (parameters != null)
                {
                    foreach (KeyValuePair<string, object> parameter in parameters)
                    {
                        scope.Set(parameter.Key, parameter.Value);
                    }
                }

                scope.Exec(text);

                if (!scope.TryGet(PythonScripts.ResultVariable, out PyObject result) || result == null)
                {
                    throw new PythonScriptException(feature, scriptName,
                        "it left no '" + PythonScripts.ResultVariable + "' value behind", null);
                }

                using (result)
                {
                    object converted = ToManaged(result);
                    if (converted is IReadOnlyDictionary<string, object> values)
                    {
                        return values;
                    }

                    throw new PythonScriptException(feature, scriptName,
                        "its '" + PythonScripts.ResultVariable + "' value is not a dictionary", null);
                }
            }
        }
        catch (PythonException exception)
        {
            string missing = FindMissingModuleName(exception.Message);
            if (missing != null)
            {
                PythonResolution resolution = PythonLibraryLocator.Resolve(null);
                throw new PythonModuleNotInstalledException(
                    feature, missing, TidyPath(Runtime.PythonDLL), CurrentVirtualEnvironment(resolution));
            }

            throw new PythonScriptException(feature, scriptName, exception.Message, exception);
        }
    }

    /// <summary>
    /// Imports each module inside the interpreter lock, recording what Python said about each one.
    /// </summary>
    /// <param name="modules">The modules to import.</param>
    /// <param name="reports">The list the results are added to.</param>
    private static void ImportModules(string[] modules, List<PythonModuleReport> reports)
    {
        using (Py.GIL())
        {
            foreach (string module in modules)
            {
                try
                {
                    using PyObject imported = Py.Import(module);
                    reports.Add(new PythonModuleReport(module, true, null));
                }
                catch (PythonException exception)
                {
                    reports.Add(new PythonModuleReport(module, false, Tidy(exception.Message)));
                }
                catch (Exception exception)
                {
                    reports.Add(new PythonModuleReport(module, false, Tidy(exception.Message)));
                }
            }
        }
    }

    /// <summary>
    /// Runs the version probe and turns what it reports into a version.
    /// </summary>
    /// <returns>The running interpreter's version, or <see langword="null"/> when the probe failed.</returns>
    private static Version ReadProbedVersion()
    {
        try
        {
            IReadOnlyDictionary<string, object> values =
                RunScript("the Python version probe", PythonScripts.ProbeVersion, null);
            return new Version(
                ToInt32(values, "major"), ToInt32(values, "minor"), ToInt32(values, "micro"));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the running interpreter's version from what it reports about itself, with no script.
    /// </summary>
    /// <returns>The version, or <see langword="null"/> when it cannot be read.</returns>
    private static Version ReadRunningVersion()
    {
        try
        {
            string text = PythonEngine.Version;
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            return PythonLibraryLocator.ParseConfiguredVersion(text.Trim().Split(' ')[0]);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a Python value to the .NET value that stands for it, recursing into dictionaries and
    /// sequences. Everything it makes is a plain .NET object, so nothing Python owns leaves the run.
    /// </summary>
    /// <param name="value">The Python value, inside the interpreter lock.</param>
    /// <returns>The .NET value.</returns>
    private static object ToManaged(PyObject value)
    {
        if (value == null || value.IsNone())
        {
            return null;
        }

        if (PyString.IsStringType(value))
        {
            return value.As<string>();
        }

        if (PyFloat.IsFloatType(value))
        {
            return value.As<double>();
        }

        if (PyInt.IsIntType(value))
        {
            using PyType type = value.GetPythonType();
            if (string.Equals(type.Name, "bool", StringComparison.Ordinal))
            {
                return value.As<bool>();
            }

            try
            {
                return value.As<long>();
            }
            catch (InvalidCastException)
            {
                //An integer too wide for 64 bits keeps its digits rather than losing them.
                return value.ToString();
            }
        }

        if (PyDict.IsDictType(value))
        {
            var mapped = new Dictionary<string, object>(StringComparer.Ordinal);
            using var dictionary = new PyDict(value);
            using PyIterable keys = dictionary.Keys();
            foreach (PyObject key in keys)
            {
                using (key)
                {
                    string name = key.ToString();
                    using PyObject item = dictionary.GetItem(key);
                    mapped[name] = ToManaged(item);
                }
            }
            return mapped;
        }

        if (PyList.IsListType(value) || PyTuple.IsTupleType(value))
        {
            var items = new List<object>();
            using var sequence = new PyIterable(value);
            foreach (PyObject item in sequence)
            {
                using (item)
                {
                    items.Add(ToManaged(item));
                }
            }
            return items;
        }

        return value.ToString();
    }

    /// <summary>
    /// Clears the Python error indicator, which <c>Shutdown</c> refuses to run with, by asking the
    /// interpreter to do the smallest thing there is. Whatever it raises has been fetched by the time it
    /// is caught, which is what clears the indicator.
    /// </summary>
    private static void ClearPendingError()
    {
        try
        {
            using (Py.GIL())
            {
                using PyModule scope = Py.CreateScope();
                scope.Exec("pass");
            }
        }
        catch (Exception)
        {
            //Fetching the error is the point; there is nothing to do with it on the way out.
        }
    }

    /// <summary>
    /// Releases anything the embedding layer is still holding on this library's behalf. Every run here
    /// disposes what it makes inside its own scope, so this is the queue flush, not a rescue.
    /// </summary>
    private static void ReleaseHeldObjects()
    {
        try
        {
            using (Py.GIL())
            {
                Finalizer.Instance.Collect();
            }
        }
        catch (Exception)
        {
            //A collection that will not run changes nothing about ending the interpreter.
        }
    }

    /// <summary>
    /// Refuses work once Python has been shut down for the process.
    /// </summary>
    /// <exception cref="InvalidOperationException">Python has been shut down.</exception>
    private static void ThrowIfShutDown()
    {
        if (_isShutDown)
        {
            throw new InvalidOperationException(ShutDownMessage);
        }
    }

    /// <summary>
    /// Adds one "not looked at" entry per module, so that a report always answers for every module asked
    /// about, whether or not an interpreter could be started.
    /// </summary>
    /// <param name="reports">The list to add to.</param>
    /// <param name="modules">The modules asked about.</param>
    /// <param name="reason">Why the module was not looked for.</param>
    private static void AddUncheckedModules(List<PythonModuleReport> reports, string[] modules, string reason)
    {
        foreach (string module in modules)
        {
            reports.Add(new PythonModuleReport(module, false, reason));
        }
    }

    /// <summary>
    /// The sentence used when nothing named a CPython shared library at all.
    /// </summary>
    /// <returns>A sentence naming every way of naming one.</returns>
    private static string NoLibraryFoundSentence()
        => "No CPython shared library was found. Set PythonOptions.LibraryPath or the "
           + PythonOptions.LibraryPathVariable + " environment variable to libpython3.XX.so /"
           + " python3XX.dll / libpython3.XX.dylib, or name a virtual environment whose base"
           + " interpreter ships one.";

    /// <summary>
    /// The sentence <see cref="PythonNotAvailableException"/> leads with for a given report.
    /// </summary>
    /// <param name="report">The report that was not usable.</param>
    /// <returns>The sentence, or <see langword="null"/> for "nothing was found at all".</returns>
    private static string DescribeLibraryProblem(PythonSupportReport report)
    {
        if (report.LibraryPath == null)
        {
            return null;
        }

        if (!report.LibraryLoads)
        {
            return "The CPython shared library at " + report.LibraryPath + " could not be loaded.";
        }

        if (report.Version == null)
        {
            return "The version of the CPython shared library at " + report.LibraryPath
                   + " could not be determined.";
        }

        return "CPython " + report.Version.ToString(2) + " at " + report.LibraryPath
               + " is outside the supported range.";
    }

    /// <summary>
    /// The virtual environment in effect, preferring what the embedding layer reports over what this
    /// library resolved, because the two differ when the host configured the interpreter.
    /// </summary>
    /// <param name="resolution">This library's own resolution.</param>
    /// <returns>The environment, or <see langword="null"/> when there is none.</returns>
    private static string CurrentVirtualEnvironment(PythonResolution resolution)
    {
        try
        {
            string reported = PythonEngine.VirtualEnvironment;
            if (!string.IsNullOrWhiteSpace(reported))
            {
                return reported;
            }
        }
        catch (Exception)
        {
            //An interpreter that will not answer leaves this library's own resolution as the answer.
        }

        return resolution.VirtualEnvironment;
    }

    /// <summary>
    /// Where the library a RUNNING interpreter loaded came from. A path that this library resolved for
    /// itself keeps the source it was resolved from; a path that only the running interpreter knows -
    /// which is what a host application that named its own library in code leaves behind - is reported
    /// as <see cref="PythonLibrarySource.Host"/> rather than as "not found with a path".
    /// </summary>
    /// <param name="resolved">What this library's own resolution found.</param>
    /// <param name="runningLibraryPath">The library the running interpreter reports, or <see langword="null"/>.</param>
    /// <returns>The source to report.</returns>
    internal static PythonLibrarySource DescribeRunningLibrarySource(
        PythonLibrarySource resolved, string runningLibraryPath)
        => resolved == PythonLibrarySource.NotFound && !string.IsNullOrWhiteSpace(runningLibraryPath)
            ? PythonLibrarySource.Host
            : resolved;

    /// <summary>
    /// Finds the module name in a "No module named 'x'" message.
    /// </summary>
    /// <param name="message">What Python said.</param>
    /// <returns>The module name, or <see langword="null"/> when the message is about something else.</returns>
    internal static string FindMissingModuleName(string message)
    {
        const string marker = "No module named '";

        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        int start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        int end = message.IndexOf('\'', start);
        return end <= start ? null : message.Substring(start, end - start);
    }

    /// <summary>
    /// Trims the caller's module names, drops the blank ones and keeps the first of any repeat.
    /// </summary>
    /// <param name="requiredModules">What the caller passed, possibly <see langword="null"/>.</param>
    /// <returns>The module names to look for, in the order asked.</returns>
    private static string[] CleanModuleNames(string[] requiredModules)
    {
        if (requiredModules == null || requiredModules.Length == 0)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cleaned = new List<string>();

        foreach (string module in requiredModules)
        {
            if (string.IsNullOrWhiteSpace(module))
            {
                continue;
            }

            string trimmed = module.Trim();
            if (seen.Add(trimmed))
            {
                cleaned.Add(trimmed);
            }
        }

        return cleaned.ToArray();
    }

    /// <summary>
    /// Collapses the relative parts of a path, so that what is reported reads the way a person would
    /// write it.
    /// </summary>
    /// <param name="path">The path to tidy, possibly <see langword="null"/>.</param>
    /// <returns>The tidied path, or <see langword="null"/>.</returns>
    private static string TidyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception)
        {
            return path.Trim();
        }
    }

    /// <summary>
    /// Renders a version as the major.minor pair a supported range is really expressed in.
    /// </summary>
    /// <param name="version">The version to render.</param>
    /// <returns>"3.10", or "(unknown)".</returns>
    private static string Describe(Version version) => version == null ? "(unknown)" : version.ToString(2);

    /// <summary>
    /// Reduces a message to one tidy line, which is what a report is read as.
    /// </summary>
    /// <param name="message">The raw message.</param>
    /// <returns>The message with its line breaks flattened.</returns>
    private static string Tidy(string message)
        => string.IsNullOrWhiteSpace(message)
            ? "(no message)"
            : message.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

    /// <summary>
    /// Reads one integer out of what a script reported.
    /// </summary>
    /// <param name="values">What the script left behind.</param>
    /// <param name="name">The entry to read.</param>
    /// <returns>The value as an integer.</returns>
    private static int ToInt32(IReadOnlyDictionary<string, object> values, string name)
        => values.TryGetValue(name, out object value) && value != null
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : 0;
}

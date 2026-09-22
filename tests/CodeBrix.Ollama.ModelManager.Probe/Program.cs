using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;

namespace CodeBrix.Ollama.ModelManager.Probe;

/// <summary>
/// Runs one whole-process observation and prints what it saw, one <c>key: value</c> line at a time.
/// The mode is the first command-line argument; an optional second one names a file to create just
/// before the process returns, which is how a watching test tells a slow start from the outcome it is
/// measuring.
/// </summary>
/// <remarks>
/// Every mode that touches Python does so from its own method, never from <see cref="Main"/>, for the
/// same reason the library keeps its embedding layer behind one class: the runtime loads an assembly when
/// it compiles a method that names a type from it, so a mode that is not run cannot load anything.
/// </remarks>
internal static class Program
{
    /// <summary>Printed on the last line of standard output, once the mode has finished.</summary>
    internal const string ReadyMarker = "READY";

    /// <summary>A full bundle cycle with no Python anywhere: the inert-dependency fence.</summary>
    internal const string InertMode = "inert";

    /// <summary>An automatic export of a bundle that already ships ONNX: the pass-through fence.</summary>
    internal const string ExportMode = "export";

    /// <summary>A whole reduction through the Python engine, in a process that then has to end.</summary>
    internal const string ReductionMode = "reduce";

    /// <summary>A whole reduction through the managed engine: the fence that says it loads no Python.</summary>
    internal const string ManagedReductionMode = "reduce-managed";

    /// <summary>The variable naming the graph the reduction mode reduces.</summary>
    internal const string ProbeModelVariable = "CODEBRIX_OLLAMA_PROBE_MODEL";

    /// <summary>This library starts the interpreter, then shuts it down explicitly.</summary>
    internal const string OwnMode = "own";

    /// <summary>The host starts the interpreter first, so this library must leave it alone.</summary>
    internal const string HostOwnedMode = "hostowned";

    /// <summary>This library starts the interpreter and nobody shuts it down.</summary>
    internal const string NoShutdownMode = "noshutdown";

    /// <summary>The host starts the interpreter naming its library in code, with no variable set.</summary>
    internal const string HostCodeMode = "hostcode";

    /// <summary>The assembly whose absence the inert fence asserts.</summary>
    internal const string PythonAssemblyName = "CodeBrix.Python";

    /// <summary>The line the inert mode prints, followed by True or False.</summary>
    internal const string LoadedPrefix = "loaded: ";

    /// <summary>The line every Python mode prints, followed by the owner.</summary>
    internal const string OwnerPrefix = "owner: ";

    /// <summary>
    /// Runs one probe.
    /// </summary>
    /// <param name="args">The mode, optionally followed by the path of a ready file.</param>
    /// <returns>0 when the mode ran, 2 when it was not recognized, 3 when it failed.</returns>
    internal static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : InertMode;
        string readyFile = args.Length > 1 ? args[1] : null;

        int exitCode;

        try
        {
            switch (mode)
            {
                case InertMode:
                    exitCode = RunInert().GetAwaiter().GetResult();
                    break;

                case ExportMode:
                    exitCode = RunExport().GetAwaiter().GetResult();
                    break;

                case ReductionMode:
                    exitCode = RunReduce().GetAwaiter().GetResult();
                    break;

                case ManagedReductionMode:
                    exitCode = RunManagedReduce().GetAwaiter().GetResult();
                    break;

                case OwnMode:
                    exitCode = RunOwn();
                    break;

                case HostOwnedMode:
                    exitCode = RunHostOwned();
                    break;

                case NoShutdownMode:
                    exitCode = RunNoShutdown();
                    break;

                case HostCodeMode:
                    exitCode = RunHostCode();
                    break;

                default:
                    Console.Error.WriteLine(
                        $"Unknown mode '{mode}'. Expected one of: {InertMode}, {ExportMode},"
                        + $" {ReductionMode}, {ManagedReductionMode}, {OwnMode}, {HostOwnedMode},"
                        + $" {NoShutdownMode}, {HostCodeMode}.");
                    return 2;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 3;
        }

        Console.WriteLine(ReadyMarker);
        Console.Out.Flush();

        if (!string.IsNullOrEmpty(readyFile))
        {
            //Written last, and only ever read after it exists, so its presence alone is the signal.
            File.WriteAllText(readyFile, mode);
        }

        return exitCode;
    }

    /// <summary>
    /// Imports a folder this process writes itself, then lists, resolves and materializes it, and reports
    /// whether the embedding layer was loaded at any point. Nothing here names a Python type.
    /// </summary>
    /// <returns>0 when the cycle ran and the assembly was NOT loaded.</returns>
    private static async Task<int> RunInert()
    {
        string root = Path.Combine(Path.GetTempPath(), "codebrix-ollama-probe-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string store = Path.Combine(root, "store");
        string target = Path.Combine(root, "materialized");

        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(source, "weights"));
            File.WriteAllText(Path.Combine(source, "config.json"), "{\"probe\":true}\n");
            File.WriteAllText(Path.Combine(source, "LICENSE"), "The terms of this probe's imaginary model.\n");
            File.WriteAllBytes(Path.Combine(source, "weights", "model.bin"), new byte[] { 1, 2, 3, 4, 5 });

            using var modelStore = new ModelStore(new ModelStoreOptions { StoreDirectory = store });

            await modelStore.ImportBundleAsync("local/probe/inert:latest", source);
            var listed = await modelStore.ListAsync();
            ResolvedModel resolved = await modelStore.ResolveAsync("local/probe/inert:latest");
            var written = await modelStore.MaterializeAsync("local/probe/inert:latest", target);

            Console.WriteLine("listed: " + listed.Count);
            Console.WriteLine("format: " + resolved.Format);
            Console.WriteLine("files: " + resolved.Files.Count);
            Console.WriteLine("materialized: " + written.Count);
        }
        finally
        {
            TryDelete(root);
        }

        bool loaded = IsPythonAssemblyLoaded();
        Console.WriteLine(LoadedPrefix + loaded);
        return loaded ? 1 : 0;
    }

    /// <summary>
    /// Imports a bundle that already ships an exported graph, exports it with the automatic route, and
    /// reports whether the embedding layer was loaded at any point. The pass-through route converts
    /// nothing, so a whole export must happen with no interpreter anywhere near the process.
    /// </summary>
    /// <returns>0 when the export took the pass-through route and the assembly was NOT loaded.</returns>
    private static async Task<int> RunExport()
    {
        string root = Path.Combine(Path.GetTempPath(), "codebrix-ollama-probe-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string store = Path.Combine(root, "store");
        string target = Path.Combine(root, "materialized");
        const string sourceName = "local/probe/export:latest";

        ExportResult result;
        int materialized;

        try
        {
            Directory.CreateDirectory(Path.Combine(source, "onnx"));
            File.WriteAllText(Path.Combine(source, "config.json"), "{\"architectures\":[\"LlamaForCausalLM\"]}\n");
            File.WriteAllText(Path.Combine(source, "LICENSE"), "The terms of this probe's imaginary model.\n");
            File.WriteAllBytes(Path.Combine(source, "model.safetensors"), new byte[] { 9, 9, 9, 9 });
            File.WriteAllBytes(Path.Combine(source, "onnx", "model.onnx"), new byte[] { 8, 9, 58, 0 });

            using var modelStore = new ModelStore(new ModelStoreOptions { StoreDirectory = store });

            await modelStore.ImportBundleAsync(sourceName, source);
            result = await modelStore.ExportToOnnxAsync(sourceName);
            var written = await modelStore.MaterializeAsync(result.Name, target);
            materialized = written.Count;
        }
        finally
        {
            TryDelete(root);
        }

        Console.WriteLine("route: " + result.RouteUsed);
        Console.WriteLine("tool: " + result.Tool);
        Console.WriteLine("exported: " + result.Files.Count);
        Console.WriteLine("materialized: " + materialized);

        bool loaded = IsPythonAssemblyLoaded();
        Console.WriteLine(LoadedPrefix + loaded);
        return !loaded && result.RouteUsed == ExportRoute.PublisherOnnx ? 0 : 1;
    }

    /// <summary>
    /// Imports a bundle holding one real graph, reduces it through the Python engine, and shuts the
    /// interpreter down. Unlike the export mode, this one IS meant to load the embedding layer: what it
    /// observes is that a process which ran a quantizer still ends by itself, and that a reduction gives
    /// back a whole bundle - the reduced graph and the configuration beside it.
    /// </summary>
    /// <returns>0 when the Python engine ran, wrote something and left a two-file bundle behind.</returns>
    private static async Task<int> RunReduce()
    {
        string graph = Environment.GetEnvironmentVariable(ProbeModelVariable);
        if (string.IsNullOrWhiteSpace(graph) || !File.Exists(graph))
        {
            Console.Error.WriteLine(
                $"The {ProbeModelVariable} variable must name an .onnx file for the {ReductionMode}"
                + " mode to reduce.");
            return 2;
        }

        string root = Path.Combine(Path.GetTempPath(), "codebrix-ollama-probe-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string store = Path.Combine(root, "store");
        const string sourceName = "local/probe/reduce:onnx";

        ReduceResult result;

        try
        {
            Directory.CreateDirectory(source);
            File.Copy(graph, Path.Combine(source, "model.onnx"));
            File.WriteAllText(Path.Combine(source, "config.json"), "{\"probe\":true}\n");

            var storeOptions = new ModelStoreOptions { StoreDirectory = store };
            storeOptions.Python.VirtualEnvironment = ProbeOptions().VirtualEnvironment;

            using var modelStore = new ModelStore(storeOptions);

            await modelStore.ImportBundleAsync(sourceName, source);
            result = await modelStore.ReduceOnnxAsync(
                sourceName,
                //Named outright: this mode is about the PYTHON engine, and what it observes is that a
                //process which started an interpreter to quantize a graph still ends by itself. The
                //managed engine has a mode of its own, which observes that it starts none.
                new ReduceOptions { Mode = ReduceMode.WeightOnlyInt4, Engine = ReduceEngine.Python });
        }
        finally
        {
            TryDelete(root);
        }

        Console.WriteLine("engine: " + result.EngineUsed);
        Console.WriteLine("mode: " + result.Mode);
        Console.WriteLine("name: " + result.Name);
        Console.WriteLine("files: " + result.Files.Count);
        Console.WriteLine("source-bytes: " + result.SourceBytes);
        Console.WriteLine("reduced-bytes: " + result.ReducedBytes);
        Console.WriteLine("tool: " + result.Tool + " " + result.ToolVersion);
        Console.WriteLine(OwnerPrefix + PythonSupport.Owner);

        PythonSupport.Shutdown();
        Console.WriteLine("initialized-after-shutdown: " + PythonSupport.IsInitialized);

        return result.EngineUsed == ReduceEngine.Python
               && result.ReducedBytes > 0
               && result.Files.Count == 2
            ? 0
            : 1;
    }

    /// <summary>
    /// Imports a bundle holding one real graph and reduces it to four-bit weights through the MANAGED engine, then
    /// reports whether the embedding layer was loaded at any point. This is the fence for the promise that making an
    /// existing graph smaller needs nothing installed: the whole reduction has to happen with no interpreter anywhere
    /// near the process.
    /// </summary>
    /// <returns>0 when the managed engine ran, wrote something, and the assembly was NOT loaded.</returns>
    private static async Task<int> RunManagedReduce()
    {
        string graph = Environment.GetEnvironmentVariable(ProbeModelVariable);
        if (string.IsNullOrWhiteSpace(graph) || !File.Exists(graph))
        {
            Console.Error.WriteLine(
                $"The {ProbeModelVariable} variable must name an .onnx file for the {ManagedReductionMode}"
                + " mode to reduce.");
            return 2;
        }

        string root = Path.Combine(Path.GetTempPath(), "codebrix-ollama-probe-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string store = Path.Combine(root, "store");
        const string sourceName = "local/probe/managed:onnx";

        ReduceResult result;

        try
        {
            Directory.CreateDirectory(source);
            File.Copy(graph, Path.Combine(source, "model.onnx"));
            File.WriteAllText(Path.Combine(source, "config.json"), "{\"probe\":true}\n");

            //No virtual environment is named, and none is needed: if anything in this path looked for an
            //interpreter, the assembly below would be loaded and this mode would fail.
            using var modelStore = new ModelStore(new ModelStoreOptions { StoreDirectory = store });

            await modelStore.ImportBundleAsync(sourceName, source);
            result = await modelStore.ReduceOnnxAsync(
                sourceName, new ReduceOptions { Mode = ReduceMode.WeightOnlyInt4, BlockSize = 32 });
        }
        finally
        {
            TryDelete(root);
        }

        Console.WriteLine("engine: " + result.EngineUsed);
        Console.WriteLine("mode: " + result.Mode);
        Console.WriteLine("name: " + result.Name);
        Console.WriteLine("files: " + result.Files.Count);
        Console.WriteLine("source-bytes: " + result.SourceBytes);
        Console.WriteLine("reduced-bytes: " + result.ReducedBytes);
        Console.WriteLine("tool: " + result.Tool + " " + result.ToolVersion);

        bool loaded = IsPythonAssemblyLoaded();
        Console.WriteLine(LoadedPrefix + loaded);

        return !loaded
               && result.EngineUsed == ReduceEngine.Managed
               && result.ReducedBytes > 0
               && result.ReducedBytes < result.SourceBytes
               && result.Files.Count == 2
            ? 0
            : 1;
    }

    /// <summary>
    /// Lets this library start the interpreter, shuts it down explicitly, and shows that Python work is
    /// refused afterwards.
    /// </summary>
    /// <returns>0 when this library owned the interpreter and ended it.</returns>
    private static int RunOwn()
    {
        PythonOptions options = ProbeOptions();

        PythonSupportReport report = PythonSupport.Check(options, "sys");
        Console.WriteLine(OwnerPrefix + report.Owner);
        Console.WriteLine("usable: " + report.IsUsable);
        Console.WriteLine("version: " + (report.Version == null ? "(unknown)" : report.Version.ToString()));
        Console.WriteLine("venv: " + (report.VirtualEnvironment ?? "(none)"));
        Console.WriteLine("venv-source: " + report.VirtualEnvironmentSource);
        Console.WriteLine("problems: " + report.Problems.Count);

        PythonSupport.Shutdown();
        PythonSupport.Shutdown();

        Console.WriteLine("initialized-after-shutdown: " + PythonSupport.IsInitialized);
        Console.WriteLine("owner-after-shutdown: " + PythonSupport.Owner);

        try
        {
            PythonSupport.Require(options, "the probe", "sys");
            Console.WriteLine("after-shutdown: (nothing was thrown)");
            return 1;
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine("after-shutdown: " + exception.Message);
        }

        return report.Owner == PythonEngineOwner.ModelManager && report.IsUsable ? 0 : 1;
    }

    /// <summary>
    /// Starts an interpreter the way a host application would, before this library has ever looked, and
    /// shows that the library adopts it and never ends it.
    /// </summary>
    /// <returns>0 when the library reported the host as the owner and left the interpreter running.</returns>
    private static int RunHostOwned()
    {
        string venv = Environment.GetEnvironmentVariable(PythonOptions.VirtualEnvironmentVariable);
        CodeBrix.Python.PythonEngine.VirtualEnvironment = venv;
        CodeBrix.Python.PythonEngine.Initialize();
        _ = CodeBrix.Python.PythonEngine.BeginAllowThreads();
        Console.WriteLine("host-initialized: " + CodeBrix.Python.PythonEngine.IsInitialized);

        PythonSupportReport report = PythonSupport.Check(ProbeOptions(), "sys");
        Console.WriteLine(OwnerPrefix + report.Owner);
        Console.WriteLine("usable: " + report.IsUsable);

        PythonSupport.Shutdown();
        Console.WriteLine("still-initialized: " + CodeBrix.Python.PythonEngine.IsInitialized);

        int exitCode = report.Owner == PythonEngineOwner.Host
                       && report.IsUsable
                       && CodeBrix.Python.PythonEngine.IsInitialized
            ? 0
            : 1;

        //The host started it, so the host ends it.
        CodeBrix.Python.PythonEngine.Shutdown();
        return exitCode;
    }

    /// <summary>
    /// Lets this library start the interpreter and deliberately never shuts it down, which is what the
    /// bounded process-exit mode exists for.
    /// </summary>
    /// <returns>0 when this library owned the interpreter.</returns>
    private static int RunNoShutdown()
    {
        PythonSupportReport report = PythonSupport.Check(ProbeOptions(), "sys");
        Console.WriteLine(OwnerPrefix + report.Owner);
        Console.WriteLine("usable: " + report.IsUsable);
        return report.Owner == PythonEngineOwner.ModelManager && report.IsUsable ? 0 : 1;
    }

    /// <summary>
    /// Starts an interpreter the way a host application that names its own environment in code would,
    /// with every variable this library reads cleared first, and shows that the library reports the
    /// library path the running interpreter states with the source Host.
    /// </summary>
    /// <returns>0 when the report named the host as both the owner and the source of the library.</returns>
    private static int RunHostCode()
    {
        string venv = Environment.GetEnvironmentVariable(PythonOptions.VirtualEnvironmentVariable);

        //Nothing this library reads may name a library, so that what it reports can only have come from
        //the interpreter the host started.
        Environment.SetEnvironmentVariable(PythonOptions.VirtualEnvironmentVariable, null);
        Environment.SetEnvironmentVariable(PythonOptions.LibraryPathVariable, null);
        Environment.SetEnvironmentVariable("PYTHONNET_VENV", null);
        Environment.SetEnvironmentVariable("VIRTUAL_ENV", null);

        CodeBrix.Python.PythonEngine.VirtualEnvironment = venv;
        CodeBrix.Python.PythonEngine.Initialize();
        _ = CodeBrix.Python.PythonEngine.BeginAllowThreads();
        Console.WriteLine("host-initialized: " + CodeBrix.Python.PythonEngine.IsInitialized);

        PythonSupportReport report = PythonSupport.Check(new PythonOptions(), "sys");
        Console.WriteLine(OwnerPrefix + report.Owner);
        Console.WriteLine("library-source: " + report.LibrarySource);
        Console.WriteLine("library-path-set: " + (report.LibraryPath != null));
        Console.WriteLine("usable: " + report.IsUsable);

        int exitCode = report.Owner == PythonEngineOwner.Host
                       && report.LibrarySource == PythonLibrarySource.Host
                       && report.LibraryPath != null
            ? 0
            : 1;

        //The host started it, so the host ends it.
        CodeBrix.Python.PythonEngine.Shutdown();
        return exitCode;
    }

    /// <summary>
    /// The options every Python mode runs with: the virtual environment this machine's tests use, named
    /// outright so that the run does not depend on how the process was launched.
    /// </summary>
    /// <returns>The options.</returns>
    private static PythonOptions ProbeOptions()
        => new PythonOptions
        {
            VirtualEnvironment = Environment.GetEnvironmentVariable(PythonOptions.VirtualEnvironmentVariable),
        };

    /// <summary>
    /// Whether the embedding layer has been loaded into this process.
    /// </summary>
    /// <returns><see langword="true"/> when an assembly of that name is loaded.</returns>
    private static bool IsPythonAssemblyLoaded()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, PythonAssemblyName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Removes the probe's temporary tree, and says nothing when it cannot.
    /// </summary>
    /// <param name="directory">The directory to remove.</param>
    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
            //A leftover temporary directory must never fail a probe.
        }
        catch (UnauthorizedAccessException)
        {
            //As above.
        }
    }
}

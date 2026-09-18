using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What a reduction knows that is not about the store: which engine a request resolves to, what a
/// derived bundle is called, which of a bundle's files are graphs and which are carried through beside
/// them, and how the three reduction scripts are run and read back.
/// </summary>
/// <remarks>
/// Nothing here names a type of the embedding layer. Choosing an engine, naming the output and working
/// out which files a reduction touches all happen without loading anything, exactly as they do for an
/// export.
/// </remarks>
internal static class OnnxReduce
{
    /// <summary>The phrase every Python message about this feature ends with.</summary>
    internal const string Feature = "reducing an ONNX model";

    /// <summary>The tool name recorded in a reduced bundle's provenance when the Python engine ran.</summary>
    internal const string Tool = "onnxruntime";

    /// <summary>The tool name recorded in a reduced bundle's provenance when the managed engine ran.</summary>
    internal const string ManagedTool = "CodeBrix.Ollama.ModelManager";

    /// <summary>The tag a dynamically quantized bundle is stored under.</summary>
    internal const string DynamicInt8Tag = "int8";

    /// <summary>The tag a weight-only eight-bit bundle is stored under.</summary>
    internal const string WeightOnlyInt8Tag = "int8-weights";

    /// <summary>The tag a weight-only four-bit bundle is stored under.</summary>
    internal const string WeightOnlyInt4Tag = "int4-weights";

    /// <summary>The tag a prepared, unquantized bundle is stored under.</summary>
    internal const string PreprocessedTag = "onnx-preprocessed";

    /// <summary>
    /// The size at which a graph is written with its weights in a file of their own. A single protocol
    /// buffer message cannot exceed two gibibytes, and this leaves room for the graph around the weights.
    /// </summary>
    internal const long ExternalDataThreshold = 1610612736L;

    /// <summary>The value a script reports when it met the two-gibibyte message limit.</summary>
    internal const string SizeLimitError = "size-limit";

    /// <summary>The modules the Python engine imports, checked before any script runs.</summary>
    internal static readonly string[] PythonModules = { "onnx", "onnxruntime" };

    /// <summary>
    /// Replaces the engine call, which is how the reduction flow around it - the files it selects, what
    /// it carries through, what it stores and what it reports - is tested on a machine with no
    /// interpreter. Only the tests set it, and they set it back in a finally.
    /// </summary>
    internal static Func<ReduceMode, ReduceOptions, string, string, Task<OnnxReduceRun>> RunOverrideForTesting
    {
        get;
        set;
    }

    /// <summary>
    /// The version of this library, which is the tool version a managed reduction records.
    /// </summary>
    /// <returns>The assembly version, or <see langword="null"/> when the assembly states none.</returns>
    internal static string ManagedToolVersion
    {
        get
        {
            Version version = typeof(OnnxReduce).Assembly.GetName().Version;
            return version == null ? null : version.ToString();
        }
    }

    /// <summary>
    /// Whether choosing an engine for this request depends on the graph having been through shape inference.
    /// </summary>
    /// <param name="requested">What the caller asked for.</param>
    /// <param name="mode">The mode being run.</param>
    /// <returns><see langword="true"/> when the marker has to be read before an engine can be chosen.</returns>
    /// <remarks>
    /// Only the automatic choice for dynamic quantization asks the question: the weight-only modes read the weights
    /// themselves and the managed engine covers them whatever the graph has been through, and an explicit engine is
    /// what the caller said.
    /// </remarks>
    internal static bool NeedsInferMarker(ReduceEngine requested, ReduceMode mode)
        => requested == ReduceEngine.Auto && mode == ReduceMode.DynamicInt8;

    /// <summary>
    /// Whether choosing an engine for this request depends on whether this machine has a usable CPython.
    /// </summary>
    /// <param name="requested">What the caller asked for.</param>
    /// <param name="mode">The mode being run.</param>
    /// <param name="hasInferMarker">Whether the graphs carry the shape-inference marker.</param>
    /// <returns><see langword="true"/> when Python has to be looked for before an engine can be chosen.</returns>
    /// <remarks>
    /// A weight-only reduction never asks, which is the promise this library makes about it: nothing is installed and
    /// nothing is looked for. Dynamic quantization asks only when the graph has not been prepared, because that is the
    /// one case the managed engine cannot take.
    /// </remarks>
    internal static bool NeedsPythonAvailability(ReduceEngine requested, ReduceMode mode, bool hasInferMarker)
        => NeedsInferMarker(requested, mode) && !hasInferMarker;

    /// <summary>
    /// Whether this machine has a CPython the Python engine could run in, asked without throwing.
    /// </summary>
    /// <param name="options">Where this process finds CPython.</param>
    /// <returns><see langword="true"/> when the library and the modules the engine needs are all there.</returns>
    /// <remarks>
    /// This is only ever called when the answer decides the engine, so that a reduction the managed engine covers
    /// never looks for an interpreter at all. A replacement engine stands in for the tools while one is set.
    /// </remarks>
    internal static bool IsPythonAvailable(PythonOptions options)
        => RunOverrideForTesting != null || PythonSupport.Check(options, PythonModules).IsUsable;

    /// <summary>
    /// The engine that will actually run, which is where every rule about choosing one lives.
    /// </summary>
    /// <param name="requested">What the caller asked for.</param>
    /// <param name="mode">The mode being run, which decides what an engine can cover.</param>
    /// <param name="hasInferMarker">
    /// Whether the graphs to reduce carry the metadata entry that records shape inference. It is read only when
    /// <see cref="NeedsInferMarker"/> says the choice depends on it.
    /// </param>
    /// <param name="isPythonAvailable">
    /// Whether this machine has a usable CPython. It is asked only when <see cref="NeedsPythonAvailability"/> says the
    /// choice depends on it.
    /// </param>
    /// <returns>The engine to run, which is never <see cref="ReduceEngine.Auto"/>.</returns>
    /// <exception cref="NotSupportedException">The managed engine was asked for a mode it does not cover.</exception>
    /// <exception cref="ModelManagerException">
    /// Nothing can run: dynamic quantization was asked for on a graph nobody has prepared, on a machine with no
    /// CPython to prepare it in.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE WEIGHT-ONLY MODES ARE ALWAYS MANAGED. Reducing a graph that already exists to four- or eight-bit weights is
    /// arithmetic over the weights themselves, this library does it, and nothing has to be installed for it.
    /// </para>
    /// <para>
    /// DYNAMIC QUANTIZATION reads the shapes a preparation pass infers, and the managed engine infers none. A graph
    /// that carries the marker has been through that pass - the preparation mode of this very library leaves it - so
    /// the managed engine takes it. A graph that has not is the Python engine's, because preparation runs there.
    /// </para>
    /// <para>
    /// PREPARATION ITSELF is Python's alone: it is shape inference and ONNX Runtime's own graph optimizer, neither of
    /// which is ported.
    /// </para>
    /// </remarks>
    internal static ReduceEngine ResolveEngine(
        ReduceEngine requested, ReduceMode mode, bool hasInferMarker, bool isPythonAvailable)
    {
        if (requested == ReduceEngine.Python)
        {
            return ReduceEngine.Python;
        }

        if (requested == ReduceEngine.Managed)
        {
            if (mode == ReduceMode.PreprocessOnly)
            {
                throw new NotSupportedException(
                    "The managed engine cannot prepare a graph for quantization: preparing one is shape inference and"
                        + " ONNX Runtime's own graph optimizer, and neither is part of this library. Leave"
                        + " ReduceOptions.Engine at ReduceEngine.Auto for ReduceMode.PreprocessOnly, which runs those"
                        + " tools in a CPython this machine has.");
            }

            return ReduceEngine.Managed;
        }

        switch (mode)
        {
            case ReduceMode.WeightOnlyInt4:
            case ReduceMode.WeightOnlyInt8:
                return ReduceEngine.Managed;

            case ReduceMode.PreprocessOnly:
                return ReduceEngine.Python;

            default:
                if (hasInferMarker)
                {
                    return ReduceEngine.Managed;
                }

                if (isPythonAvailable)
                {
                    return ReduceEngine.Python;
                }

                throw new ModelManagerException(
                    "dynamic eight-bit quantization reads the shapes a preparation pass infers, and this graph carries"
                        + " no record of having been through one. The managed engine infers no shapes and there is no"
                        + " usable CPython on this machine to infer them in. Prepare the model first with"
                        + " ReduceMode.PreprocessOnly on a machine that has one, or choose ReduceMode.WeightOnlyInt8"
                        + " or ReduceMode.WeightOnlyInt4, which need nothing installed");
        }
    }

    /// <summary>
    /// Checks that the engine that is about to run can run at all, before a single file is laid out.
    /// </summary>
    /// <param name="options">Where this process finds CPython.</param>
    /// <param name="engine">The engine that will run.</param>
    /// <exception cref="PythonNotAvailableException">There is no usable CPython.</exception>
    /// <exception cref="PythonModuleNotInstalledException">A module the engine needs is not installed.</exception>
    /// <remarks>
    /// A replacement engine stands in for the tools, so nothing is required of the machine while one is
    /// set: that is what lets the flow around the arithmetic be tested where there is no interpreter.
    /// </remarks>
    internal static void RequireEngine(PythonOptions options, ReduceEngine engine)
    {
        if (engine != ReduceEngine.Python || RunOverrideForTesting != null)
        {
            return;
        }

        PythonSupport.Require(options, Feature, PythonModules);
    }

    /// <summary>
    /// The tag a mode's output is stored under when the caller names no output.
    /// </summary>
    /// <param name="mode">The mode that ran.</param>
    /// <returns>The tag.</returns>
    internal static string TagFor(ReduceMode mode)
    {
        switch (mode)
        {
            case ReduceMode.WeightOnlyInt8:
                return WeightOnlyInt8Tag;
            case ReduceMode.WeightOnlyInt4:
                return WeightOnlyInt4Tag;
            case ReduceMode.PreprocessOnly:
                return PreprocessedTag;
            default:
                return DynamicInt8Tag;
        }
    }

    /// <summary>
    /// The tag a derived bundle takes: the source's own tag with the mode's tag added to it, so that a
    /// name says everything that has been done to the graphs it holds.
    /// </summary>
    /// <remarks>
    /// A source that carries no tag of its own, or only the default one, takes the mode's tag alone
    /// (<c>hf.co/x/y</c> becomes <c>hf.co/x/y:int4-weights</c>); anything else is appended to with a
    /// hyphen (<c>hf.co/x/y:onnx</c> becomes <c>hf.co/x/y:onnx-int4-weights</c>). A tag is never
    /// repeated: a suffix that already begins with the source's tag IS the new tag, which is what makes
    /// the prepared form of <c>:onnx</c> read <c>:onnx-preprocessed</c> rather than saying onnx twice.
    /// </remarks>
    /// <param name="sourceTag">The source model's tag.</param>
    /// <param name="suffix">The mode's tag.</param>
    /// <returns>The derived tag.</returns>
    internal static string AppendTag(string sourceTag, string suffix)
    {
        string tag = (sourceTag ?? string.Empty).Trim();

        if (tag.Length == 0 || string.Equals(tag, ModelName.DefaultTag, StringComparison.OrdinalIgnoreCase))
        {
            return suffix;
        }

        return suffix.StartsWith(tag + "-", StringComparison.Ordinal) ? suffix : tag + "-" + suffix;
    }

    /// <summary>
    /// The graphs a reduction runs over: the files the caller named, or every <c>.onnx</c> file the
    /// bundle holds.
    /// </summary>
    /// <param name="files">The source bundle's files.</param>
    /// <param name="requested">The paths the caller named, or <see langword="null"/> for all of them.</param>
    /// <returns>The relative paths to reduce, in the order the bundle holds them.</returns>
    /// <exception cref="ModelManagerException">
    /// A named file is not in the bundle or is not a graph, or the bundle holds no graph at all.
    /// </exception>
    internal static IReadOnlyList<string> SelectFiles(
        IReadOnlyList<ResolvedFile> files, IReadOnlyList<string> requested)
    {
        var available = new List<string>();
        foreach (ResolvedFile file in files)
        {
            if (OnnxExport.IsOnnxFile(file.Name))
            {
                available.Add(file.Name);
            }
        }

        if (requested == null || requested.Count == 0)
        {
            if (available.Count == 0)
            {
                throw new ModelManagerException(
                    "this model holds no .onnx file, so there is nothing to reduce; export it to ONNX"
                        + " first with ExportToOnnxAsync");
            }

            return available;
        }

        var selected = new List<string>();
        foreach (string name in requested)
        {
            string wanted = (name ?? string.Empty).Trim().Replace('\\', '/');
            if (!available.Contains(wanted))
            {
                throw new ModelManagerException(
                    "this model holds no .onnx file called '" + wanted + "'; the graphs it holds are "
                        + (available.Count == 0 ? "(none)" : string.Join(", ", available)));
            }
            if (!selected.Contains(wanted))
            {
                selected.Add(wanted);
            }
        }

        //Manifest order, whatever order the caller named them in.
        var ordered = new List<string>();
        foreach (string name in available)
        {
            if (selected.Contains(name))
            {
                ordered.Add(name);
            }
        }
        return ordered;
    }

    /// <summary>
    /// Whether one of a bundle's files holds the weights of a graph that is being reduced. ONNX's own
    /// convention names such a file after the graph it belongs to - <c>model.onnx.data</c>,
    /// <c>model.onnx_data</c> - so a file that begins with a selected graph's name belongs to it and is
    /// replaced along with it.
    /// </summary>
    /// <param name="path">The file to judge, as the bundle spells it.</param>
    /// <param name="graphPath">The graph's path, as the bundle spells it.</param>
    /// <returns><see langword="true"/> when the file holds that graph's weights.</returns>
    internal static bool IsExternalDataFor(string path, string graphPath)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(graphPath))
        {
            return false;
        }

        return path.Length > graphPath.Length
            && path.StartsWith(graphPath, StringComparison.OrdinalIgnoreCase)
            && (path[graphPath.Length] == '.' || path[graphPath.Length] == '_');
    }

    /// <summary>
    /// The files a reduction carries through unchanged: everything the source holds except the graphs
    /// being reduced, the files holding those graphs' weights, and the checkpoints an export already
    /// replaced. A configuration, a tokenizer, a model card or a graph nobody asked to reduce is part of
    /// using the model, so it stays.
    /// </summary>
    /// <param name="files">The source bundle's files.</param>
    /// <param name="selected">The graphs being reduced.</param>
    /// <returns>The files to carry through, in the order the bundle holds them.</returns>
    internal static IReadOnlyList<ResolvedFile> CompanionFiles(
        IReadOnlyList<ResolvedFile> files, IReadOnlyList<string> selected)
    {
        var kept = new List<ResolvedFile>();
        if (files == null)
        {
            return kept;
        }

        foreach (ResolvedFile file in files)
        {
            if (IsReplaced(file.Name, selected) || OnnxExport.IsSupersededCheckpoint(file.Name))
            {
                continue;
            }
            kept.Add(file);
        }
        return kept;
    }

    /// <summary>
    /// Whether a file is one of the graphs being reduced, or holds one of their weights.
    /// </summary>
    /// <param name="path">The file to judge.</param>
    /// <param name="selected">The graphs being reduced.</param>
    /// <returns><see langword="true"/> when the reduction writes this file itself.</returns>
    private static bool IsReplaced(string path, IReadOnlyList<string> selected)
    {
        foreach (string graph in selected)
        {
            if (string.Equals(path, graph, StringComparison.Ordinal) || IsExternalDataFor(path, graph))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The size of a graph on disk: the file itself and any file of weights written beside it.
    /// </summary>
    /// <param name="path">The absolute path of the graph.</param>
    /// <returns>The total size in bytes, or 0 when the file is not there.</returns>
    internal static long MeasureGraph(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return 0;
        }

        long total = file.Length;
        string folder = file.DirectoryName;
        if (string.IsNullOrEmpty(folder))
        {
            return total;
        }

        foreach (string candidate in Directory.GetFiles(folder, file.Name + "*"))
        {
            if (!string.Equals(candidate, file.FullName, StringComparison.Ordinal)
                && IsExternalDataFor(Path.GetFileName(candidate), file.Name))
            {
                total += new FileInfo(candidate).Length;
            }
        }

        return total;
    }

    /// <summary>
    /// The absolute paths of the files of weights written beside a graph.
    /// </summary>
    /// <param name="path">The absolute path of the graph.</param>
    /// <returns>The side files, which is usually none.</returns>
    internal static IReadOnlyList<string> ExternalDataPaths(string path)
    {
        var found = new List<string>();
        var file = new FileInfo(path);
        string folder = file.DirectoryName;

        if (!file.Exists || string.IsNullOrEmpty(folder))
        {
            return found;
        }

        foreach (string candidate in Directory.GetFiles(folder, file.Name + "*"))
        {
            if (!string.Equals(candidate, file.FullName, StringComparison.Ordinal)
                && IsExternalDataFor(Path.GetFileName(candidate), file.Name))
            {
                found.Add(candidate);
            }
        }

        return found;
    }

    /// <summary>
    /// Replaces the files of weights beside a graph with copies of themselves, so that each is a file
    /// with ONE link to its content.
    /// </summary>
    /// <remarks>
    /// THE ONNX PACKAGE REFUSES TO READ A FILE OF WEIGHTS THAT HAS MORE THAN ONE HARD LINK, calling it a
    /// hard-link attack, and this library lays a bundle out by hard-linking it because that is what makes
    /// laying out gigabytes free. The two cannot both be had: a graph whose weights are in a file of
    /// their own is copied into the working folder instead, which costs one pass over the bytes and is
    /// paid only by the models that have such a file. The graph itself is still a link.
    /// </remarks>
    /// <param name="path">The absolute path of the graph that is about to be read.</param>
    internal static void UnlinkExternalData(string path)
    {
        foreach (string side in ExternalDataPaths(path))
        {
            string copy = side + ".copying";
            try
            {
                File.Copy(side, copy, true);
                File.Delete(side);
                File.Move(copy, side);
            }
            finally
            {
                if (File.Exists(copy))
                {
                    File.Delete(copy);
                }
            }
        }
    }

    /// <summary>
    /// Whether a graph of this size has to be written with its weights in a file of their own.
    /// </summary>
    /// <param name="bytes">The size of what is about to be written.</param>
    /// <returns><see langword="true"/> when the two-gibibyte message limit is within reach.</returns>
    internal static bool UseExternalData(long bytes) => bytes > ExternalDataThreshold;

    /// <summary>
    /// Whether the managed engine writes a reduced graph with its weights in a file of their own.
    /// </summary>
    /// <param name="graphBytes">The size on disk of the graph that was read, and of any weights beside it.</param>
    /// <param name="quantizedBytes">The size of the tensors the quantized model holds in memory.</param>
    /// <returns><see langword="true"/> when the weights go to a file of their own.</returns>
    /// <remarks>
    /// The first question is the one the Python engine asks, about the same graph, so that the two engines write
    /// the same file: that engine looks at what it was handed, not at what came out, and a graph small enough to
    /// be written in one piece is written in one piece whether or not it arrived with its weights beside it. The
    /// second is this engine's own: it builds the whole message in memory, and a single protocol buffer message
    /// cannot exceed two gibibytes, so a model whose tensors alone approach that has to be written the other way.
    /// Quantizing only ever makes a model smaller, so the second question can answer yes only where the first
    /// already has.
    /// </remarks>
    internal static bool UseExternalDataForManaged(long graphBytes, long quantizedBytes)
        => UseExternalData(graphBytes) || UseExternalData(quantizedBytes);

    /// <summary>
    /// Whether a mode prepares the graph before quantizing it.
    /// </summary>
    /// <param name="mode">The mode being run.</param>
    /// <param name="options">The options the caller gave.</param>
    /// <returns><see langword="true"/> when a preprocessing pass runs first.</returns>
    /// <remarks>
    /// Only dynamic quantization reads the shapes that preprocessing infers. The weight-only modes read
    /// the weights themselves, so preparing a graph for them costs time and changes the graph for no
    /// gain; <see cref="ReduceMode.PreprocessOnly"/> is the way to prepare one on purpose.
    /// </remarks>
    internal static bool PreprocessesFor(ReduceMode mode, ReduceOptions options)
        => mode == ReduceMode.DynamicInt8 && options.Preprocess;

    /// <summary>
    /// The number of bits per weight value a weight-only mode stores.
    /// </summary>
    /// <param name="mode">The mode being run.</param>
    /// <returns>4 or 8.</returns>
    internal static int BitsFor(ReduceMode mode) => mode == ReduceMode.WeightOnlyInt4 ? 4 : 8;

    /// <summary>
    /// The settings a derived bundle records about the reduction that produced it.
    /// </summary>
    /// <param name="mode">The mode that ran.</param>
    /// <param name="engine">The engine that ran.</param>
    /// <param name="options">The options the caller gave.</param>
    /// <param name="selected">The graphs that were reduced.</param>
    /// <returns>The settings, as strings.</returns>
    internal static IReadOnlyDictionary<string, string> SettingsFor(
        ReduceMode mode,
        ReduceEngine engine,
        ReduceOptions options,
        IReadOnlyList<string> selected)
    {
        bool weightOnly = mode == ReduceMode.WeightOnlyInt4 || mode == ReduceMode.WeightOnlyInt8;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = mode.ToString(),
            ["engine"] = engine.ToString(),
            ["requestedEngine"] = options.Engine.ToString(),
            ["preprocess"] = PreprocessesFor(mode, options) ? "true" : "false",
            ["blockSize"] = weightOnly
                ? options.BlockSize.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            ["isSymmetric"] = weightOnly && options.IsSymmetric ? "true" : weightOnly ? "false" : string.Empty,
            ["accuracyLevel"] = weightOnly && options.AccuracyLevel.HasValue
                ? options.AccuracyLevel.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            ["files"] = string.Join(",", selected)
        };
    }

    /// <summary>
    /// Runs one graph through the engine: the preparation pass when the mode wants one, and then the
    /// quantizer, writing into the output path the caller names.
    /// </summary>
    /// <param name="pythonOptions">Where this process finds CPython.</param>
    /// <param name="engine">The engine that was chosen for this reduction.</param>
    /// <param name="mode">The mode to run.</param>
    /// <param name="options">The options the caller gave.</param>
    /// <param name="inputPath">The absolute path of the graph to reduce.</param>
    /// <param name="outputPath">The absolute path to write the reduced graph to.</param>
    /// <param name="stageDirectory">A folder the preparation pass may write its intermediate graph into.</param>
    /// <param name="cancellationToken">A token that cancels the wait for the interpreter.</param>
    /// <returns>What the tool reported.</returns>
    /// <exception cref="PythonNotAvailableException">There is no usable CPython.</exception>
    /// <exception cref="PythonModuleNotInstalledException">A module the engine needs is not installed.</exception>
    /// <exception cref="PythonScriptException">The tool refused the graph, or failed.</exception>
    /// <exception cref="ModelManagerException">The graph is too large for the preparation pass to write.</exception>
    /// <exception cref="NotSupportedException">The managed engine cannot take this graph.</exception>
    internal static async Task<OnnxReduceRun> RunAsync(
        PythonOptions pythonOptions,
        ReduceEngine engine,
        ReduceMode mode,
        ReduceOptions options,
        string inputPath,
        string outputPath,
        string stageDirectory,
        CancellationToken cancellationToken)
    {
        Func<ReduceMode, ReduceOptions, string, string, Task<OnnxReduceRun>> replacement = RunOverrideForTesting;
        if (replacement != null)
        {
            return await replacement(mode, options, inputPath, outputPath).ConfigureAwait(false);
        }

        if (engine == ReduceEngine.Managed)
        {
            return await RunManagedAsync(mode, options, inputPath, outputPath, cancellationToken)
                .ConfigureAwait(false);
        }

        string quantizerInput = inputPath;

        if (mode == ReduceMode.PreprocessOnly)
        {
            return await RunPreprocessAsync(
                pythonOptions, inputPath, outputPath, cancellationToken).ConfigureAwait(false);
        }

        if (PreprocessesFor(mode, options))
        {
            Directory.CreateDirectory(stageDirectory);
            quantizerInput = Path.Combine(stageDirectory, Path.GetFileName(inputPath));
            await RunPreprocessAsync(pythonOptions, inputPath, quantizerInput, cancellationToken)
                .ConfigureAwait(false);
        }

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["input_path"] = quantizerInput,
            ["output_path"] = outputPath,
            ["use_external_data"] = UseExternalData(MeasureGraph(quantizerInput))
        };

        string scriptName;
        if (mode == ReduceMode.DynamicInt8)
        {
            scriptName = PythonScripts.ReduceDynamic;
        }
        else
        {
            scriptName = PythonScripts.ReduceWeightOnly;
            parameters["bits"] = BitsFor(mode);
            parameters["block_size"] = options.BlockSize;
            parameters["is_symmetric"] = options.IsSymmetric;
            parameters["accuracy_level"] = options.AccuracyLevel ?? -1;
        }

        IReadOnlyDictionary<string, object> reported = await PythonHost.RunScriptAsync(
            pythonOptions, Feature, scriptName, parameters, PythonModules, cancellationToken)
            .ConfigureAwait(false);

        return ReadRun(scriptName, outputPath, reported);
    }

    /// <summary>
    /// Runs one graph through the managed engine: reads it, pulls in any weights kept beside it, quantizes it in
    /// memory and writes what came out.
    /// </summary>
    /// <param name="mode">The mode to run.</param>
    /// <param name="options">The options the caller gave.</param>
    /// <param name="inputPath">The absolute path of the graph to reduce.</param>
    /// <param name="outputPath">The absolute path to write the reduced graph to.</param>
    /// <param name="cancellationToken">A token that cancels the reads and writes.</param>
    /// <returns>What the engine wrote.</returns>
    /// <exception cref="NotSupportedException">The mode, or something in the graph, is outside what it covers.</exception>
    /// <remarks>
    /// NOTHING HERE TOUCHES PYTHON, and that is the point of the engine: no interpreter is started, no module is
    /// imported and the one package this library depends on is never loaded.
    /// </remarks>
    private static async Task<OnnxReduceRun> RunManagedAsync(
        ReduceMode mode,
        ReduceOptions options,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (mode == ReduceMode.PreprocessOnly)
        {
            throw new NotSupportedException(
                "The managed engine cannot prepare a graph for quantization; only the Python engine can.");
        }

        //Measured before anything is read in, because it is the same number the Python engine measures to
        //decide the same thing, and the two engines have to decide it the same way.
        long graphBytes = MeasureGraph(inputPath);

        OnnxModel model = await OnnxModel.ReadAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (HasExternalTensors(model))
        {
            await model.LoadExternalDataAsync(cancellationToken).ConfigureAwait(false);
        }

        if (mode == ReduceMode.DynamicInt8)
        {
            OnnxDynamicQuantizer.Process(model, DynamicOptionsFor());
        }
        else
        {
            OnnxMatMulNBitsQuantizer.Process(model, WeightOnlyOptionsFor(mode, options));
        }

        var saveOptions = new OnnxSaveOptions
        {
            UseExternalData = UseExternalDataForManaged(graphBytes, MeasureModel(model)),
            //The ONNX package's own threshold, measured the way that package measures it, so that a graph written
            //here holds the same tensors in its side file as one written by the tools.
            SizeThreshold = OnnxQuantizationUtilities.ExternalDataRawSizeThreshold,
        };

        await model.WriteAsync(outputPath, saveOptions, cancellationToken).ConfigureAwait(false);

        var files = new List<string> { outputPath };
        long total = new FileInfo(outputPath).Length;
        foreach (string side in ExternalDataPaths(outputPath))
        {
            files.Add(side);
            total += new FileInfo(side).Length;
        }

        return new OnnxReduceRun(ManagedTool, ManagedToolVersion, files, total);
    }

    /// <summary>
    /// The managed weight-only settings one of this library's modes maps to.
    /// </summary>
    /// <param name="mode">The mode being run.</param>
    /// <param name="options">The options the caller gave.</param>
    /// <returns>The settings to quantize with.</returns>
    internal static OnnxWeightOnlyQuantizationOptions WeightOnlyOptionsFor(ReduceMode mode, ReduceOptions options)
        => new OnnxWeightOnlyQuantizationOptions
        {
            Bits = BitsFor(mode),
            BlockSize = options.BlockSize,
            IsSymmetric = options.IsSymmetric,
            AccuracyLevel = options.AccuracyLevel,
        };

    /// <summary>
    /// The managed dynamic settings this library's dynamic mode maps to, which are the tools' own defaults and the
    /// same two operator types the Python engine is asked for.
    /// </summary>
    /// <returns>The settings to quantize with.</returns>
    internal static OnnxDynamicQuantizationOptions DynamicOptionsFor() => new OnnxDynamicQuantizationOptions();

    /// <summary>
    /// Whether any of a model's tensors keeps its bytes in a file beside the graph.
    /// </summary>
    /// <param name="model">The model to look at.</param>
    /// <returns><see langword="true"/> when at least one tensor does.</returns>
    internal static bool HasExternalTensors(OnnxModel model)
    {
        foreach (OnnxTensorProto tensor in model.EnumerateTensors())
        {
            if (tensor.HasExternalData)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// How many bytes of tensor data a model in memory holds, which is what decides whether it can be written as one
    /// message at all.
    /// </summary>
    /// <param name="model">The model to measure.</param>
    /// <returns>The total size of every tensor's bytes.</returns>
    /// <remarks>
    /// Counted as a 64-bit number on purpose: the question being asked is whether the answer would overflow the
    /// 32-bit size a single protocol buffer message is limited to, so it cannot be asked with a 32-bit count.
    /// </remarks>
    internal static long MeasureModel(OnnxModel model)
    {
        long total = 0;
        foreach (OnnxTensorProto tensor in model.EnumerateTensors())
        {
            if (tensor.RawData != null)
            {
                total += tensor.RawData.Length;
            }
        }

        return total;
    }

    /// <summary>
    /// Runs the preparation pass over one graph.
    /// </summary>
    /// <param name="pythonOptions">Where this process finds CPython.</param>
    /// <param name="inputPath">The absolute path of the graph to prepare.</param>
    /// <param name="outputPath">The absolute path to write the prepared graph to.</param>
    /// <param name="cancellationToken">A token that cancels the wait for the interpreter.</param>
    /// <returns>What the tool reported.</returns>
    /// <exception cref="ModelManagerException">The prepared graph is too large to write.</exception>
    private static async Task<OnnxReduceRun> RunPreprocessAsync(
        PythonOptions pythonOptions,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["input_path"] = inputPath,
            ["output_path"] = outputPath,
            ["use_external_data"] = UseExternalData(MeasureGraph(inputPath))
        };

        IReadOnlyDictionary<string, object> reported = await PythonHost.RunScriptAsync(
            pythonOptions, Feature, PythonScripts.ReducePreprocess, parameters, PythonModules,
            cancellationToken).ConfigureAwait(false);

        if (reported.TryGetValue("error", out object error) && error != null
            && string.Equals(error.ToString(), SizeLimitError, StringComparison.Ordinal))
        {
            throw new ModelManagerException(
                "the graph " + Path.GetFileName(inputPath) + " cannot be prepared for quantization"
                    + " because a prepared graph is written as one protocol buffer message and that"
                    + " cannot exceed 2 GiB. Reduce it with one of the weight-only modes, which need no"
                    + " preparation, or set ReduceOptions.Preprocess to false. The tool said: "
                    + ReadString(reported, "message"));
        }

        return ReadRun(PythonScripts.ReducePreprocess, outputPath, reported);
    }

    /// <summary>
    /// Turns what a script reported into a run result.
    /// </summary>
    /// <param name="scriptName">The script that reported it, for the message.</param>
    /// <param name="outputPath">The path the script was asked to write, for the message.</param>
    /// <param name="reported">What the script left behind.</param>
    /// <returns>The run result.</returns>
    /// <exception cref="PythonScriptException">The script wrote no file at all.</exception>
    private static OnnxReduceRun ReadRun(
        string scriptName, string outputPath, IReadOnlyDictionary<string, object> reported)
    {
        string tool = ReadString(reported, "tool") ?? Tool;
        string version = ReadString(reported, "version");

        var files = new List<string>();
        if (reported.TryGetValue("files", out object reportedFiles) && reportedFiles is IReadOnlyList<object> list)
        {
            foreach (object file in list)
            {
                if (file != null)
                {
                    files.Add(file.ToString());
                }
            }
        }

        long total = 0;
        if (reported.TryGetValue("sizes", out object reportedSizes) && reportedSizes is IReadOnlyList<object> sizes)
        {
            foreach (object size in sizes)
            {
                total += ToInt64(size);
            }
        }

        if (files.Count == 0)
        {
            throw new PythonScriptException(
                Feature, scriptName, "it wrote no file to " + outputPath, null);
        }

        return new OnnxReduceRun(tool, string.IsNullOrWhiteSpace(version) ? null : version, files, total);
    }

    /// <summary>
    /// Reads one string out of what a script reported.
    /// </summary>
    /// <param name="reported">What the script left behind.</param>
    /// <param name="name">The entry to read.</param>
    /// <returns>The value, or <see langword="null"/> when it is absent or empty.</returns>
    private static string ReadString(IReadOnlyDictionary<string, object> reported, string name)
    {
        if (!reported.TryGetValue(name, out object value) || value == null)
        {
            return null;
        }

        string text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Reads one size out of what a script reported, whatever numeric shape it arrived in.
    /// </summary>
    /// <param name="value">The reported value.</param>
    /// <returns>The size in bytes, or 0 when it is not a number.</returns>
    private static long ToInt64(object value)
    {
        if (value is long number)
        {
            return number;
        }

        return value != null
            && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : 0;
    }
}

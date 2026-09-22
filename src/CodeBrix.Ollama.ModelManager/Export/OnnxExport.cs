using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// What an export to ONNX knows that is not about the store: which route a model calls for, which
/// Python modules each route needs, and how the two export scripts are run and read back.
/// </summary>
/// <remarks>
/// Nothing here names a type of the embedding layer. The Python routes go through
/// <see cref="PythonHost"/> like every other Python call in this library, so choosing a route, naming
/// the files a pass-through would take and working out the default output name all happen without
/// loading anything.
/// </remarks>
internal static class OnnxExport
{
    /// <summary>The phrase every Python message about this feature ends with.</summary>
    internal const string Feature = "exporting to ONNX";

    /// <summary>The tool name recorded when the publisher's own files are registered unchanged.</summary>
    internal const string PublisherTool = "publisher";

    /// <summary>The tool name recorded when the ONNX Runtime GenAI model builder wrote the files.</summary>
    internal const string GenAiTool = "onnxruntime-genai";

    /// <summary>The tool name recorded when Optimum's exporter wrote the files.</summary>
    internal const string OptimumTool = "optimum";

    /// <summary>The tag a derived bundle is stored under when the caller names nothing.</summary>
    internal const string OnnxTag = "onnx";

    /// <summary>The execution provider the builder writes for. Only the CPU one is exported here.</summary>
    internal const string ExecutionProvider = "cpu";

    /// <summary>
    /// The task Optimum works out for itself. It is only ever passed when nothing here could work the
    /// task out, because Optimum CANNOT infer a task from a LOCAL FOLDER - it says so and lists every
    /// task it knows, which is a better message than anything this library could invent.
    /// </summary>
    internal const string AutomaticTask = "auto";

    /// <summary>The task a decoder-only language model is exported for.</summary>
    internal const string TextGenerationTask = "text-generation-with-past";

    /// <summary>The configuration file whose <c>architectures</c> decide the automatic route.</summary>
    internal const string ConfigFileName = "config.json";

    /// <summary>The modules the GenAI builder route imports, checked before the script runs.</summary>
    internal static readonly string[] GenAiModules = { "onnxruntime_genai", "torch", "transformers", "onnx" };

    /// <summary>The modules the Optimum route imports, checked before the script runs.</summary>
    internal static readonly string[] OptimumModules = { "optimum", "onnx", "torch", "transformers" };

    internal static readonly string[] MuseCocoModules = { "torch", "numpy", "onnx" };

    internal static bool IsMuseCoco(ExportRoute route)
        => route == ExportRoute.MuseCocoMusic || route == ExportRoute.MuseCocoText;

    /// <summary>
    /// The architectures the ONNX Runtime GenAI model builder writes, spelled as a checkpoint's
    /// <c>config.json</c> spells them. The automatic route reads that file and takes the builder when it
    /// names one of these and Optimum when it does not.
    /// </summary>
    /// <remarks>
    /// It is what the builder supported when this was written, not a promise about what it supports now:
    /// a newer builder writes more, and naming <see cref="ExportRoute.GenAiBuilder"/> outright tries it
    /// whatever this list says. A builder that will not write a model says so itself, in the message the
    /// export carries back.
    /// </remarks>
    private static readonly HashSet<string> BuilderArchitectures = new HashSet<string>(StringComparer.Ordinal)
    {
        "ChatGLMForConditionalGeneration",
        "ChatGLMModel",
        "Ernie4_5ForCausalLM",
        "Gemma2ForCausalLM",
        "Gemma3ForCausalLM",
        "Gemma3ForConditionalGeneration",
        "GemmaForCausalLM",
        "GptOssForCausalLM",
        "GraniteForCausalLM",
        "HunYuanDenseV1ForCausalLM",
        "InternLM2ForCausalLM",
        "Lfm2ForCausalLM",
        "LlamaForCausalLM",
        "Mistral3ForConditionalGeneration",
        "MistralForCausalLM",
        "NemotronForCausalLM",
        "OlmoForCausalLM",
        "Phi3ForCausalLM",
        "Phi3SmallForCausalLM",
        "Phi3VForCausalLM",
        "Phi4MMForCausalLM",
        "PhiForCausalLM",
        "PhiMoEForCausalLM",
        "Qwen2ForCausalLM",
        "Qwen2_5_VLForConditionalGeneration",
        "Qwen3ForCausalLM",
        "Qwen3VLForConditionalGeneration",
        "SmolLM3ForCausalLM",
        "VideoChatFlashQwenForCausalLM",
        "WhisperForConditionalGeneration"
    };

    /// <summary>
    /// The modules a route needs, or an empty array for the route that needs no Python.
    /// </summary>
    /// <param name="route">The route that is about to run.</param>
    /// <returns>The module names.</returns>
    internal static string[] ModulesFor(ExportRoute route)
    {
        if (IsMuseCoco(route))
        {
            return MuseCocoModules;
        }
        if (route == ExportRoute.GenAiBuilder)
        {
            return GenAiModules;
        }
        return route == ExportRoute.Optimum ? OptimumModules : Array.Empty<string>();
    }

    /// <summary>
    /// The tool name a route records in a derived bundle's provenance.
    /// </summary>
    /// <param name="route">The route that ran.</param>
    /// <returns>The tool name.</returns>
    internal static string ToolFor(ExportRoute route)
    {
        if (IsMuseCoco(route))
        {
            return "codebrix-musecoco";
        }
        if (route == ExportRoute.GenAiBuilder)
        {
            return GenAiTool;
        }
        return route == ExportRoute.Optimum ? OptimumTool : PublisherTool;
    }

    /// <summary>
    /// Whether a bundle path names an exported graph.
    /// </summary>
    /// <param name="path">The publisher's relative path, with forward slashes.</param>
    /// <returns><see langword="true"/> for a <c>.onnx</c> file.</returns>
    internal static bool IsOnnxFile(string path)
        => !string.IsNullOrEmpty(path) && path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a bundle path names a checkpoint of another framework - the thing an export REPLACES.
    /// A pass-through leaves these behind and keeps everything else, because a configuration, a
    /// tokenizer or an external-data file is part of using the graph.
    /// </summary>
    /// <param name="path">The publisher's relative path, with forward slashes.</param>
    /// <returns><see langword="true"/> when the file is a checkpoint an export would have replaced.</returns>
    internal static bool IsSupersededCheckpoint(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        int slash = path.LastIndexOf('/');
        string name = (slash < 0 ? path : path.Substring(slash + 1)).ToLowerInvariant();

        return name.EndsWith(".safetensors", StringComparison.Ordinal)
            || name.EndsWith(".bin", StringComparison.Ordinal)
            || name.EndsWith(".pt", StringComparison.Ordinal)
            || name.EndsWith(".pth", StringComparison.Ordinal)
            || name.EndsWith(".h5", StringComparison.Ordinal)
            || name.EndsWith(".msgpack", StringComparison.Ordinal)
            || name.EndsWith(".ckpt", StringComparison.Ordinal)
            || name.Contains(".ckpt.", StringComparison.Ordinal);
    }

    /// <summary>
    /// The files a pass-through export registers: every file of the source except the checkpoints the
    /// graph replaces.
    /// </summary>
    /// <param name="files">The source bundle's files.</param>
    /// <param name="referencedWeights">Names referenced by ONNX external tensors, including checkpoint-like filenames.</param>
    /// <returns>The files to register, in the order the source holds them.</returns>
    internal static IReadOnlyList<ResolvedFile> PassThroughFiles(
        IReadOnlyList<ResolvedFile> files, ISet<string> referencedWeights = null)
    {
        var kept = new List<ResolvedFile>();
        if (files == null)
        {
            return kept;
        }

        foreach (ResolvedFile file in files)
        {
            if ((referencedWeights != null && referencedWeights.Contains(file.Name)) || !IsSupersededCheckpoint(file.Name))
            {
                kept.Add(file);
            }
        }
        return kept;
    }

    /// <summary>
    /// Whether any of a bundle's files is an exported graph, which is what makes a pass-through possible.
    /// </summary>
    /// <param name="files">The source bundle's files.</param>
    /// <returns><see langword="true"/> when at least one file is a <c>.onnx</c>.</returns>
    internal static bool HasOnnxFiles(IReadOnlyList<ResolvedFile> files)
    {
        if (files == null)
        {
            return false;
        }

        foreach (ResolvedFile file in files)
        {
            if (IsOnnxFile(file.Name))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The architectures a checkpoint's <c>config.json</c> names.
    /// </summary>
    /// <param name="configJson">The text of the file, or <see langword="null"/> when there is none.</param>
    /// <returns>The architecture names, which is empty when the file says nothing or cannot be read.</returns>
    internal static IReadOnlyList<string> ReadArchitectures(string configJson)
    {
        var architectures = new List<string>();
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return architectures;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(configJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("architectures", out JsonElement value)
                || value.ValueKind != JsonValueKind.Array)
            {
                return architectures;
            }

            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    architectures.Add(item.GetString());
                }
            }
        }
        catch (JsonException)
        {
            //A configuration file that is not JSON decides nothing; the route falls through to Optimum.
        }

        return architectures;
    }

    /// <summary>
    /// Whether the GenAI model builder is known to write one of these architectures.
    /// </summary>
    /// <param name="architectures">What a checkpoint's configuration names.</param>
    /// <returns><see langword="true"/> when the builder writes the first architecture it recognizes.</returns>
    internal static bool NamesBuilderArchitecture(IReadOnlyList<string> architectures)
    {
        if (architectures == null)
        {
            return false;
        }

        foreach (string architecture in architectures)
        {
            if (architecture != null && BuilderArchitectures.Contains(architecture))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The Optimum task a checkpoint is exported for, worked out from the architecture its
    /// <c>config.json</c> names.
    /// </summary>
    /// <remarks>
    /// Optimum's own inference reads the Hugging Face Hub and REFUSES A LOCAL FOLDER, which is all this
    /// library ever hands it, so the task is worked out here from the suffix every transformers
    /// architecture name carries. An architecture whose suffix is not one of these falls back to
    /// <see cref="AutomaticTask"/>, and Optimum's own message then lists every task it knows.
    /// </remarks>
    /// <param name="architectures">What the checkpoint's configuration names.</param>
    /// <returns>The task to export for.</returns>
    internal static string TaskFor(IReadOnlyList<string> architectures)
    {
        if (architectures != null)
        {
            foreach (string architecture in architectures)
            {
                string task = TaskForArchitecture(architecture);
                if (task != null)
                {
                    return task;
                }
            }
        }

        return AutomaticTask;
    }

    /// <summary>
    /// The task one architecture name asks for, or <see langword="null"/> when its suffix says nothing.
    /// </summary>
    /// <param name="architecture">The architecture name, as a configuration spells it.</param>
    /// <returns>The task, or <see langword="null"/>.</returns>
    private static string TaskForArchitecture(string architecture)
    {
        if (string.IsNullOrWhiteSpace(architecture))
        {
            return null;
        }

        if (architecture.EndsWith("ForCausalLM", StringComparison.Ordinal)
            || architecture.EndsWith("LMHeadModel", StringComparison.Ordinal))
        {
            return TextGenerationTask;
        }
        if (architecture.EndsWith("ForConditionalGeneration", StringComparison.Ordinal)
            || architecture.EndsWith("ForSeq2SeqLM", StringComparison.Ordinal))
        {
            return "text2text-generation-with-past";
        }
        if (architecture.EndsWith("ForSequenceClassification", StringComparison.Ordinal))
        {
            return "text-classification";
        }
        if (architecture.EndsWith("ForTokenClassification", StringComparison.Ordinal))
        {
            return "token-classification";
        }
        if (architecture.EndsWith("ForQuestionAnswering", StringComparison.Ordinal))
        {
            return "question-answering";
        }
        if (architecture.EndsWith("ForMaskedLM", StringComparison.Ordinal))
        {
            return "fill-mask";
        }
        if (architecture.EndsWith("ForMultipleChoice", StringComparison.Ordinal))
        {
            return "multiple-choice";
        }
        if (architecture.EndsWith("ForImageClassification", StringComparison.Ordinal))
        {
            return "image-classification";
        }

        return null;
    }

    /// <summary>
    /// Chooses the route an automatic export takes.
    /// </summary>
    /// <param name="files">The source bundle's files.</param>
    /// <param name="configJson">The text of the source's <c>config.json</c>, or <see langword="null"/>.</param>
    /// <returns>
    /// <see cref="ExportRoute.PublisherOnnx"/> when the bundle already ships a graph,
    /// <see cref="ExportRoute.GenAiBuilder"/> when its configuration names an architecture the builder
    /// writes, and <see cref="ExportRoute.Optimum"/> otherwise.
    /// </returns>
    internal static ExportRoute ChooseRoute(IReadOnlyList<ResolvedFile> files, string configJson)
    {
        if (HasOnnxFiles(files))
        {
            return ExportRoute.PublisherOnnx;
        }

        IReadOnlyList<string> architectures = ReadArchitectures(configJson);
        foreach (string architecture in architectures)
        {
            if (architecture == "BertForAttributModel")
            {
                return ExportRoute.MuseCocoText;
            }
        }
        return NamesBuilderArchitecture(architectures)
            ? ExportRoute.GenAiBuilder
            : ExportRoute.Optimum;
    }

    /// <summary>
    /// The settings a derived bundle records about the export that produced it.
    /// </summary>
    /// <param name="route">The route that ran.</param>
    /// <param name="options">The options the caller gave.</param>
    /// <returns>The settings, as strings.</returns>
    internal static IReadOnlyDictionary<string, string> SettingsFor(ExportRoute route, ExportOptions options)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["route"] = route.ToString(),
            ["requestedRoute"] = options.Route.ToString(),
            ["precision"] = options.Precision,
            ["allowRemoteCode"] = options.AllowRemoteCode ? "true" : "false",
            ["executionProvider"] = route == ExportRoute.GenAiBuilder ? ExecutionProvider : string.Empty
        };

    /// <summary>
    /// Runs the export script one of the Python routes uses, after checking that the modules it imports
    /// are installed.
    /// </summary>
    /// <param name="pythonOptions">Where this process finds CPython.</param>
    /// <param name="route">The route to run; it must be a Python one.</param>
    /// <param name="sourceName">The model being exported, for the builder's own messages.</param>
    /// <param name="inputDirectory">The folder the source was laid out in.</param>
    /// <param name="outputDirectory">The folder the tool writes into.</param>
    /// <param name="cacheDirectory">A folder the tool may use for its own scratch files.</param>
    /// <param name="options">The options the caller gave.</param>
    /// <param name="task">The Optimum task; ignored by the builder route.</param>
    /// <param name="cancellationToken">A token that cancels the wait for the interpreter.</param>
    /// <returns>What the tool reported.</returns>
    /// <exception cref="PythonNotAvailableException">There is no usable CPython.</exception>
    /// <exception cref="PythonModuleNotInstalledException">A module the route needs is not installed.</exception>
    /// <exception cref="PythonScriptException">The tool refused the model, or failed.</exception>
    internal static async Task<OnnxExportRun> RunAsync(
        PythonOptions pythonOptions,
        ExportRoute route,
        string sourceName,
        string inputDirectory,
        string outputDirectory,
        string cacheDirectory,
        ExportOptions options,
        string task,
        CancellationToken cancellationToken)
    {
        string scriptName = IsMuseCoco(route) ? PythonScripts.ExportMuseCoco : route == ExportRoute.GenAiBuilder
            ? PythonScripts.ExportGenAi
            : PythonScripts.ExportOptimum;

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["input_path"] = inputDirectory,
            ["output_path"] = outputDirectory,
            ["allow_remote_code"] = options.AllowRemoteCode
        };

        if (IsMuseCoco(route))
        {
            parameters["model_kind"] = route == ExportRoute.MuseCocoMusic ? "music" : "text";
            parameters["precision"] = options.Precision;
            parameters["attribute_schema_json"] = MuseCocoAssets.Read("musecoco-attributes.json");
            parameters["music_vocabulary_json"] = MuseCocoAssets.Read("musecoco-vocabulary.json");
        }
        else if (route == ExportRoute.GenAiBuilder)
        {
            parameters["model_name"] = sourceName;
            parameters["precision"] = options.Precision;
            parameters["execution_provider"] = ExecutionProvider;
            parameters["cache_dir"] = cacheDirectory;
        }
        else
        {
            parameters["task"] = string.IsNullOrWhiteSpace(task) ? AutomaticTask : task;
        }

        IReadOnlyDictionary<string, object> reported = await PythonHost.RunScriptAsync(
            pythonOptions, Feature, scriptName, parameters, ModulesFor(route), cancellationToken)
            .ConfigureAwait(false);

        return ReadRun(scriptName, ToolFor(route), reported);
    }

    /// <summary>
    /// Turns what a script reported into a run result.
    /// </summary>
    /// <param name="scriptName">The script that reported it, for the message.</param>
    /// <param name="expectedTool">The tool the route runs, used when the script names none.</param>
    /// <param name="reported">What the script left behind.</param>
    /// <returns>The run result.</returns>
    /// <exception cref="PythonScriptException">The script reported no file at all.</exception>
    private static OnnxExportRun ReadRun(
        string scriptName, string expectedTool, IReadOnlyDictionary<string, object> reported)
    {
        string tool = ReadString(reported, "tool") ?? expectedTool;
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
                Feature, scriptName, "it wrote no file into the output folder", null);
        }

        return new OnnxExportRun(tool, string.IsNullOrWhiteSpace(version) ? null : version, files, total);
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

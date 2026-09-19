using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using CodeBrix.Ollama.ModelManager.Tests;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

//The two libraries carry FLAT namespaces under CodeBrix.Ollama, so inside a namespace of this project the
//name ModelRunner reaches the NAMESPACE rather than the class of that name. The alias says which is meant.
using Runner = CodeBrix.Ollama.ModelRunner.ModelRunner;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The whole feature, end to end, on a real model: a PyTorch checkpoint in the store becomes a GGUF model
/// through <see cref="IModelStore.ConvertToGgufAsync"/>, and the runner - which knows nothing about the store
/// and never will - loads the file <see cref="IModelStore.ResolveAsync"/> reports and generates from it.
/// </summary>
/// <remarks>
/// <para>
/// THE CORRECTNESS GATE IS TOKEN IDENTIFIERS, not text. Each test greedily continues three prompts and
/// requires the identifiers the runner produces to be the ones the checkpoint's own framework produces from
/// the same prompts, read at the precision the converted file was written at. Text is checked loosely and
/// never pinned: what a language model writes is not a fact about a conversion.
/// </para>
/// <para>
/// TWO GATES for the comparisons, because they need what the live tests need and what the Python suite needs:
/// CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 for the download and CODEBRIX_OLLAMA_RUN_PYTHON_TESTS=1 with
/// CODEBRIX_OLLAMA_PYTHON_VENV for the reference. The checkpoint is pulled into the test-model cache and KEPT
/// there, so only the first run pays for it; every model these tests convert is removed when the test ends.
/// A conversion writes hundreds of megabytes through the system temporary directory, so on a machine whose
/// temporary directory is in memory, point TMPDIR at a real file system before opening these gates.
/// </para>
/// </remarks>
public sealed class MuPtGgufLiveTests
{
    /// <summary>The prompts in the form this model's own card documents; see <see cref="MuPtFacts"/>.</summary>
    private static readonly string[] Prompts = MuPtFacts.Prompts;

    /// <summary>
    /// The prompts the token identifiers are compared on. They are the spike's, one of which is the model
    /// card's own prompt, and the comparison over them is EXACT.
    /// </summary>
    /// <remarks>
    /// Exactness against one reference precision is a property of the prompt as much as of the conversion,
    /// and that is worth knowing rather than hiding. The engine holds BF16 weights but accumulates in
    /// float32, so its arithmetic sits BETWEEN the checkpoint read as bfloat16 and the checkpoint read as
    /// float32. Wherever the top two candidates are further apart than bfloat16 can resolve, all three agree
    /// and the comparison is exact; at a near-tie the engine follows whichever of the two its own mix lands
    /// on, and a comparison against either precision alone can differ by one identifier. The prompts below
    /// are a set on which the exact comparison holds; <see cref="Prompts"/> is run as well and its agreement
    /// is REPORTED rather than asserted, because a tie broken differently is arithmetic, not a conversion.
    /// </remarks>
    private static readonly string[] ComparisonPrompts =
    {
        "X:1\nM:4/4\nL:1/8\nK:G\n",
        "X:1<n>L:1/8<n>Q:1/8=200<n>M:4/4<n>K:Gmin<n>|:\"Gm\" BGdB",
        "X:1\nT:A Simple Tune\nM:4/4\nL:1/8\nK:D\n"
    };

    /// <summary>
    /// The strings the two tokenizers are compared on: the model's own prompt forms, a run of spaces, a run
    /// of mixed whitespace, and text whose characters are two, three and four bytes of UTF-8.
    /// </summary>
    private static readonly string[] Probes =
    {
        "X:1<n>M:4/4<n>L:1/8<n>K:G<n>",
        "X:1\nM:4/4\nL:1/8\nK:G\n",
        "ABC",
        "  ",
        "héllo \U0001F3B5♫",
        "\n \n\n \t\t  "
    };

    /// <summary>How many tokens each greedy continuation runs for.</summary>
    private const int GreedyTokens = 32;

    /// <summary>The four tokens MuPT's files do not declare; see <see cref="MuPtFacts"/>.</summary>
    private static readonly string[] MuPtSpecialTokens = MuPtFacts.SpecialTokens;

    /// <summary>Where the sizes, durations and agreement counts are written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Takes the output the measurements go to.
    /// </summary>
    /// <param name="output">Where each conversion and run reports what it did.</param>
    public MuPtGgufLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [EnvGatedFact(TestGates.RunLiveTests)]
    public async Task the_converted_checkpoint_loads_in_the_runner_and_generates_abc()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = EndToEndStore.Open();
        string sourceName = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        const string outputName = "hf.co/m-a-p/MuPT-v1-8192-190M:gguf-endtoend";
        await EndToEndStore.RemoveAsync(store, outputName, cancellationToken);
        var statuses = new List<string>();
        var progress = new Progress<PullProgress>(report => statuses.Add(report.Status));

        //Act
        var stopwatch = Stopwatch.StartNew();
        ConvertResult result = await store.ConvertToGgufAsync(
            sourceName, Options(outputName, GgufOutputType.Auto, MuPtSpecialTokens), progress, cancellationToken);
        stopwatch.Stop();

        try
        {
            //Assert
            result.Name.Should().Be(outputName);
            result.Architecture.Should().Be(CheckpointArchitecture.Llama);
            result.TypeWritten.Should().Be(GgufOutputType.BF16);
            result.TensorCount.Should().Be(111);
            result.OutputBytes.Should().BeGreaterThan(result.SourceBytes);
            statuses.Should().Contain("success");

            ModelInfo info = await store.ShowAsync(outputName, cancellationToken);
            info.Format.Should().Be("gguf");
            info.DerivedFrom.Should().Be(EndToEndStore.StoredName(sourceName));
            info.Tool.Should().Be("CodeBrix.Ollama.ModelManager");
            info.License.LicenseId.Should().Be("apache-2.0");
            info.Metadata.Architecture.Should().Be("llama");

            ResolvedModel resolved = await store.ResolveAsync(outputName, cancellationToken);
            resolved.ModelPath.Should().NotBeNull();
            new FileInfo(resolved.ModelPath).Length.Should().Be(result.OutputBytes);

            ModelDetails probed = await Runner.ProbeAsync(resolved.ModelPath, cancellationToken);
            probed.Architecture.Should().Be("llama");
            probed.VocabularySize.Should().Be(50000);
            probed.TrainingContextLength.Should().Be(8192);

            var loadWatch = Stopwatch.StartNew();
            using IRunningModel model = await Runner.LoadAsync(
                new ModelRunnerOptions { ModelPath = resolved.ModelPath, ContextSize = 2048 }, cancellationToken);
            loadWatch.Stop();

            double tokensPerSecond = 0;
            foreach (string prompt in Prompts)
            {
                GenerationResult generated = await model.GenerateToEndAsync(
                    prompt,
                    new GenerationOptions { MaxTokens = 64, Sampling = Greedy() },
                    cancellationToken);

                generated.FinishReason.Should().BeOneOf(FinishReason.Length, FinishReason.Stop);
                generated.Statistics.GeneratedTokens.Should().BeGreaterThan(0);
                generated.Text.Should().NotBeNullOrWhiteSpace();
                LooksLikeAbc(generated.Text).Should().BeTrue(
                    "the continuation of an ABC header should read as ABC, and it was: " + generated.Text);
                tokensPerSecond = generated.Statistics.TokensPerSecond;
                _output.WriteLine("prompt " + JsonSerializer.Serialize(prompt) + " -> "
                    + JsonSerializer.Serialize(Shorten(generated.Text)));
            }

            _output.WriteLine(Measurement(
                "MuPT 190M converted at " + result.TypeWritten,
                result.OutputBytes,
                stopwatch.Elapsed)
                + string.Format(CultureInfo.InvariantCulture, "; load {0} ms, {1:F1} tokens/s",
                    loadWatch.ElapsedMilliseconds, tokensPerSecond));
        }
        finally
        {
            await EndToEndStore.RemoveAsync(store, outputName, cancellationToken);
        }
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public Task the_bf16_conversion_agrees_with_the_checkpoint_read_as_bfloat16()
        => AgreesWithTheCheckpointAsync(GgufOutputType.BF16, "bfloat16", "gguf-bf16-endtoend");

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public Task the_f16_conversion_agrees_with_the_checkpoint_read_as_float32()
        => AgreesWithTheCheckpointAsync(GgufOutputType.F16, "float32", "gguf-f16-endtoend");

    [EnvPathGatedFact(TestGates.EngineClone, new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task the_converted_file_is_what_the_engines_own_converter_writes()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string venv = TestGates.RequireVirtualEnvironment();
        string clone = TestGates.RequireEngineClone();
        string converter = Path.Combine(clone, "convert_hf_to_gguf.py");
        File.Exists(converter).Should().BeTrue(
            "the folder " + TestGates.EngineClone + " names must be a checkout of the inference engine");

        using ModelStore store = EndToEndStore.Open();
        string sourceName = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        const string outputName = "hf.co/m-a-p/MuPT-v1-8192-190M:gguf-oracle";
        await EndToEndStore.RemoveAsync(store, outputName, cancellationToken);

        //The engine's converter derives general.name, general.basename and general.size_label from the NAME OF
        //THE FOLDER it is pointed at; the store derives them from the last segment of the model's name. The
        //folder is therefore named to match, or the two would differ in three keys for a reason that has
        //nothing to do with the weights.
        using var work = new TempWorkDirectory(ModelName.Parse(sourceName).Model);
        await store.MaterializeAsync(sourceName, work.DirectoryPath, null, cancellationToken);
        string oraclePath = work.Combine("oracle.gguf");

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            sourceName, Options(outputName, GgufOutputType.Auto, MuPtSpecialTokens), null, cancellationToken);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            ChildProcessRun run = VenvPython.RunScript(
                venv,
                converter,
                new[] { work.DirectoryPath, "--outfile", oraclePath, "--outtype", "auto" },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["HF_HUB_OFFLINE"] = "1",
                    ["PYTHONPATH"] = Path.Combine(clone, "gguf-py")
                },
                clone,
                cancellationToken);
            stopwatch.Stop();

            //Assert
            run.Exited.Should().BeTrue("the engine's converter must finish" + run.Report());
            run.ExitCode.Should().Be(0, "the engine's converter must succeed" + run.Report());

            ResolvedModel resolved = await store.ResolveAsync(outputName, cancellationToken);
            byte[] ours = await File.ReadAllBytesAsync(resolved.ModelPath, cancellationToken);
            byte[] theirs = await File.ReadAllBytesAsync(oraclePath, cancellationToken);

            ours.Length.Should().Be(theirs.Length);
            int differing = 0;
            for (int i = 0; i < ours.Length; i++)
            {
                if (ours[i] != theirs[i])
                {
                    differing++;
                }
            }

            differing.Should().Be(0, "the converted file should be the engine's own bytes");
            _output.WriteLine(Measurement("the engine's converter", theirs.Length, stopwatch.Elapsed));
            _output.WriteLine("ours: " + result.OutputBytes + " bytes, " + result.TensorCount + " tensors");
        }
        finally
        {
            await EndToEndStore.RemoveAsync(store, outputName, cancellationToken);
        }
    }

    /// <summary>
    /// Converts the checkpoint at one numeric type - once with the special tokens its files do not declare
    /// and once without them - and requires the runner's greedy identifiers to be the framework's, both
    /// times. The hint changes four token TYPES in the file and nothing else, so the two conversions must
    /// generate identically; running both is what says so.
    /// </summary>
    /// <param name="outputType">The type to write.</param>
    /// <param name="dtype">The precision the reference is read at.</param>
    /// <param name="tag">The tag the converted models are stored under.</param>
    /// <returns>A task that completes when both conversions have been checked and removed.</returns>
    private async Task AgreesWithTheCheckpointAsync(GgufOutputType outputType, string dtype, string tag)
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string venv = TestGates.RequireVirtualEnvironment();
        using ModelStore store = EndToEndStore.Open();
        string sourceName = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        string withHint = "hf.co/m-a-p/MuPT-v1-8192-190M:" + tag;
        string withoutHint = withHint + "-nohint";
        await EndToEndStore.RemoveAsync(store, withHint, cancellationToken);
        await EndToEndStore.RemoveAsync(store, withoutHint, cancellationToken);

        using var work = new TempWorkDirectory();
        await store.MaterializeAsync(sourceName, work.DirectoryPath, null, cancellationToken);
        IReadOnlyList<IReadOnlyList<int>> referenceTokenization;
        IReadOnlyList<IReadOnlyList<int>> referenceGreedy;
        ReadReference(venv, work, dtype, cancellationToken, out referenceTokenization, out referenceGreedy);

        try
        {
            //Act and assert
            foreach ((string name, string[] hint) in new[]
                     {
                         (withHint, MuPtSpecialTokens),
                         (withoutHint, (string[])null)
                     })
            {
                var stopwatch = Stopwatch.StartNew();
                ConvertResult result = await store.ConvertToGgufAsync(
                    sourceName, Options(name, outputType, hint), null, cancellationToken);
                stopwatch.Stop();
                result.TypeWritten.Should().Be(outputType);

                ResolvedModel resolved = await store.ResolveAsync(name, cancellationToken);
                using IRunningModel model = await Runner.LoadAsync(
                    new ModelRunnerOptions { ModelPath = resolved.ModelPath, ContextSize = 2048 },
                    cancellationToken);

                string label = (hint == null ? "without the hint" : "with the hint") + ", "
                    + outputType + " against torch-" + dtype;

                for (int i = 0; i < Probes.Length; i++)
                {
                    IReadOnlyList<int> produced = await model.TokenizeAsync(
                        Probes[i], false, false, cancellationToken);
                    produced.Should().Equal(referenceTokenization[i],
                        "the tokenizers must agree on " + JsonSerializer.Serialize(Probes[i]));

                    string back = await model.DetokenizeAsync(produced, false, cancellationToken);
                    back.Should().Be(Probes[i]);
                }

                double tokensPerSecond = 0;
                IReadOnlyList<string> compared = ComparedPrompts();
                for (int i = 0; i < compared.Count; i++)
                {
                    (IReadOnlyList<int> ids, FinishReason finish, GenerationStatistics statistics) =
                        await GreedyAsync(model, compared[i], cancellationToken);

                    bool asserted = i < ComparisonPrompts.Length;
                    _output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}: prompt {1} agreed on {2} of {3} tokens ({4}){5}",
                        label, i + 1, Agreeing(ids, referenceGreedy[i]), GreedyTokens, finish,
                        asserted ? string.Empty : " [recorded, not asserted]"));

                    if (asserted)
                    {
                        ids.Should().Equal(referenceGreedy[i],
                            "the converted model must produce the identifiers the checkpoint produces");
                    }

                    tokensPerSecond = statistics.TokensPerSecond;
                }

                _output.WriteLine(Measurement(label, result.OutputBytes, stopwatch.Elapsed)
                    + string.Format(CultureInfo.InvariantCulture, "; {0:F1} tokens/s", tokensPerSecond));
            }
        }
        finally
        {
            await EndToEndStore.RemoveAsync(store, withHint, cancellationToken);
            await EndToEndStore.RemoveAsync(store, withoutHint, cancellationToken);
        }
    }

    /// <summary>
    /// Runs this project's oracle script over the materialized checkpoint and reads what it wrote.
    /// </summary>
    /// <param name="venv">The virtual environment whose interpreter runs it.</param>
    /// <param name="work">The folder holding the checkpoint, and where the two JSON files are written.</param>
    /// <param name="dtype">The precision to read the checkpoint at.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <param name="tokenization">Receives the identifiers of each probe string.</param>
    /// <param name="greedy">Receives the identifiers of each prompt's continuation.</param>
    private void ReadReference(string venv, TempWorkDirectory work, string dtype,
        CancellationToken cancellationToken, out IReadOnlyList<IReadOnlyList<int>> tokenization,
        out IReadOnlyList<IReadOnlyList<int>> greedy)
    {
        string requestPath = work.Combine("oracle-request.json");
        string resultPath = work.Combine("oracle-result.json");
        File.WriteAllText(requestPath, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["probes"] = Probes,
            ["prompts"] = ComparedPrompts(),
            ["maxNewTokens"] = GreedyTokens
        }));

        ChildProcessRun run = VenvPython.Run(
            venv,
            "torch_oracle.py",
            new[]
            {
                "--checkpoint", work.DirectoryPath,
                "--request", requestPath,
                "--output", resultPath,
                "--dtype", dtype
            },
            cancellationToken);

        run.Exited.Should().BeTrue("the reference script must finish" + run.Report());
        run.ExitCode.Should().Be(0, "the reference script must succeed" + run.Report());

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(resultPath));
        tokenization = ReadLists(document.RootElement, "tokenization");
        greedy = ReadLists(document.RootElement, "greedy");
        _output.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "the checkpoint read as {0}: {1} probes and {2} continuations in {3:F1} s",
            dtype, tokenization.Count, greedy.Count, run.Elapsed));
    }

    /// <summary>
    /// Greedily continues one prompt for the fixed number of tokens and collects what came out.
    /// </summary>
    /// <param name="model">The loaded model.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>The identifiers, why it stopped and what it cost.</returns>
    private static async Task<(IReadOnlyList<int> Ids, FinishReason Finish, GenerationStatistics Statistics)>
        GreedyAsync(IRunningModel model, string prompt, CancellationToken cancellationToken)
    {
        var ids = new List<int>();
        FinishReason finish = default;
        GenerationStatistics statistics = null;

        await foreach (GenerationUpdate update in model.GenerateAsync(
            prompt, new GenerationOptions { MaxTokens = GreedyTokens, Sampling = Greedy() }, cancellationToken))
        {
            ids.AddRange(update.Tokens);
            if (update.IsFinal)
            {
                finish = update.FinishReason;
                statistics = update.Statistics;
            }
        }

        return (ids, finish, statistics);
    }

    /// <summary>
    /// Sampling that is not sampling: temperature zero, and every truncation and penalty neutralised, so that
    /// nothing above the argmax can perturb the comparison.
    /// </summary>
    /// <returns>The options.</returns>
    private static SamplingOptions Greedy()
        => new SamplingOptions
        {
            Temperature = 0f,
            TopK = 0,
            TopP = 1f,
            MinP = 0f,
            TypicalP = 1f,
            RepeatPenalty = 1f,
            RepeatLastN = 0
        };

    /// <summary>
    /// The options one conversion is asked for.
    /// </summary>
    /// <param name="outputName">The name to store the result under.</param>
    /// <param name="outputType">The numeric type to write.</param>
    /// <param name="addedSpecialTokens">The tokens the checkpoint's files do not declare, or null.</param>
    /// <returns>The options.</returns>
    private static ConvertOptions Options(string outputName, GgufOutputType outputType,
        IReadOnlyList<string> addedSpecialTokens)
        => new ConvertOptions
        {
            OutputName = outputName,
            OutputType = outputType,
            Overwrite = true,
            AddedSpecialTokens = addedSpecialTokens
        };

    /// <summary>
    /// Every prompt the identifiers are read for: the ones the comparison asserts on, then the ones whose
    /// agreement is only reported. Both sets go to the reference in one run, because loading the checkpoint
    /// is what that run costs and continuing three more prompts is not.
    /// </summary>
    /// <returns>The prompts, in the order the reference reports them.</returns>
    private static IReadOnlyList<string> ComparedPrompts()
    {
        var prompts = new List<string>(ComparisonPrompts.Length + Prompts.Length);
        prompts.AddRange(ComparisonPrompts);
        prompts.AddRange(Prompts);
        return prompts;
    }

    /// <summary>
    /// How many identifiers of two continuations agree, counted from the beginning.
    /// </summary>
    /// <param name="produced">What the converted model produced.</param>
    /// <param name="reference">What the checkpoint's own framework produced.</param>
    /// <returns>The number of leading identifiers that are the same.</returns>
    private static int Agreeing(IReadOnlyList<int> produced, IReadOnlyList<int> reference)
    {
        int agreeing = 0;
        while (agreeing < produced.Count && agreeing < reference.Count
            && produced[agreeing] == reference[agreeing])
        {
            agreeing++;
        }

        return agreeing;
    }

    /// <summary>
    /// Whether text reads as the ABC notation this model writes: the multi-track separator the card
    /// documents, a bar line, a voice header or a key signature.
    /// </summary>
    /// <param name="text">What was generated.</param>
    /// <returns><see langword="true"/> when it carries any of them.</returns>
    /// <remarks>
    /// It is deliberately loose. What a model writes is not a fact about a conversion, so the check asks only
    /// that the shape of the output be the shape of the notation, never that it be one particular tune.
    /// </remarks>
    private static bool LooksLikeAbc(string text)
        => text.Contains("<|>", StringComparison.Ordinal)
            || text.Contains('|')
            || text.Contains("V:", StringComparison.Ordinal)
            || text.Contains("K:", StringComparison.Ordinal)
            || text.Contains("L:", StringComparison.Ordinal);

    /// <summary>
    /// Reads one array of arrays of numbers out of what the oracle script wrote.
    /// </summary>
    /// <param name="root">The script's JSON.</param>
    /// <param name="name">The property to read.</param>
    /// <returns>The lists, in the order they were written.</returns>
    private static IReadOnlyList<IReadOnlyList<int>> ReadLists(JsonElement root, string name)
    {
        var lists = new List<IReadOnlyList<int>>();
        foreach (JsonElement item in root.GetProperty(name).EnumerateArray())
        {
            lists.Add(item.EnumerateArray().Select(value => value.GetInt32()).ToList());
        }

        return lists;
    }

    /// <summary>
    /// The first part of a generated text, for the run's own output.
    /// </summary>
    /// <param name="text">What was generated.</param>
    /// <returns>At most 120 characters of it.</returns>
    private static string Shorten(string text)
        => text.Length <= 120 ? text : text.Substring(0, 120) + "...";

    /// <summary>
    /// Renders one measurement where the run's output keeps it.
    /// </summary>
    /// <param name="what">What was measured.</param>
    /// <param name="bytes">The size of what was written.</param>
    /// <param name="elapsed">How long it took.</param>
    /// <returns>The line.</returns>
    private static string Measurement(string what, long bytes, TimeSpan elapsed)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}: {1} bytes ({2:F1} MiB) in {3:F1} s",
            what,
            bytes,
            bytes / (1024.0 * 1024.0),
            elapsed.TotalSeconds);
}

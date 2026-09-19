using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
/// The quantization feature end to end on a real model, and THE ONE PLACE THE TWO PACKAGES MEET. The store
/// finds the file, names the result and records where it came from; the runner does the quantizing; and the
/// line of code that joins them - a lambda handed to <see cref="QuantizeGgufOptions.Quantizer"/> - is written
/// here, in a test, because that is where a consumer writes it too.
/// </summary>
/// <remarks>
/// <para>
/// NEITHER LIBRARY REFERENCES THE OTHER, and this file is what that costs: three lines in the consumer's own
/// code. It is also what it buys - a store that works with no inference engine present, and a runner that
/// needs no store.
/// </para>
/// <para>
/// GREEDY AGREEMENT WITH THE UNQUANTIZED MODEL IS RECORDED, NOT ASSERTED. Quantizing changes the arithmetic
/// on purpose: a Q4_K_M model is a different model that is meant to be nearly the same one. How nearly is
/// worth measuring and worth printing, and pinning it would be pinning a number that has no right answer.
/// What IS asserted is that the file is smaller, that it loads, that it generates ABC, and - under one more
/// gate - that it is byte for byte what the engine's own quantizer writes.
/// </para>
/// <para>
/// A quantization writes hundreds of megabytes through the system temporary directory, so on a machine whose
/// temporary directory is in memory, point TMPDIR at a real file system before opening these gates.
/// </para>
/// </remarks>
public sealed class MuPtQuantizeLiveTests
{
    /// <summary>The prompts in the form this model's own card documents; see <see cref="MuPtFacts"/>.</summary>
    private static readonly string[] Prompts = MuPtFacts.Prompts;

    /// <summary>The four tokens MuPT's files do not declare; see <see cref="MuPtFacts"/>.</summary>
    private static readonly string[] MuPtSpecialTokens = MuPtFacts.SpecialTokens;

    /// <summary>The types the byte-identity comparison covers.</summary>
    private static readonly GgufQuantizationType[] ComparedTypes =
    {
        GgufQuantizationType.Q8_0,
        GgufQuantizationType.Q4_0,
        GgufQuantizationType.Q4_K_M,
        GgufQuantizationType.Q5_K_M,
        GgufQuantizationType.Q6_K
    };

    /// <summary>How many tokens each greedy continuation runs for.</summary>
    private const int GreedyTokens = 32;

    /// <summary>The name the unquantized source is converted under.</summary>
    private const string SourceName = "hf.co/m-a-p/MuPT-v1-8192-190M:gguf-quantsource";

    /// <summary>Where the sizes, durations and agreement counts are written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Takes the output the measurements go to.
    /// </summary>
    /// <param name="output">Where each quantization and run reports what it did.</param>
    public MuPtQuantizeLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// The whole road: a checkpoint becomes a GGUF model, the store quantizes it through the runner, and the
    /// runner loads the smaller file and writes ABC from it.
    /// </summary>
    [EnvGatedFact(TestGates.RunLiveTests)]
    public async Task the_quantized_model_loads_in_the_runner_and_generates_abc()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = EndToEndStore.Open();
        await EnsureSourceAsync(store, cancellationToken);

        try
        {
            IReadOnlyList<int> reference = await GreedyOfAsync(store, SourceName, cancellationToken);

            foreach (string type in new[] { "q4_k_m", "q8_0" })
            {
                string outputName = "hf.co/m-a-p/MuPT-v1-8192-190M:gguf-" + type + "-endtoend";
                await EndToEndStore.RemoveAsync(store, outputName, cancellationToken);
                var progress = new List<string>();

                //Act - THE CONSUMER WIRING, and the only place the two packages meet.
                var options = new QuantizeGgufOptions
                {
                    Type = type,
                    OutputName = outputName,
                    Overwrite = true,
                    Tool = "CodeBrix.Ollama.ModelRunner",
                    ToolVersion = Runner.GetNativeRuntimeInfo().BuildInfo,
                    Quantizer = (input, output, token) => Runner.QuantizeAsync(
                        input, output, TypeOf(type), null, token)
                };

                var stopwatch = Stopwatch.StartNew();
                QuantizeGgufResult result = await store.QuantizeGgufAsync(
                    SourceName, options, new Progress<PullProgress>(report => progress.Add(report.Status)),
                    cancellationToken);
                stopwatch.Stop();

                try
                {
                    //Assert
                    result.Name.Should().Be(outputName);
                    result.Type.Should().Be(type);
                    result.SourceName.Should().Be(SourceName);
                    (result.OutputBytes < result.SourceBytes).Should().BeTrue();
                    progress.Should().Contain("success");

                    ModelInfo info = await store.ShowAsync(outputName, cancellationToken);
                    info.Format.Should().Be("gguf");
                    info.DerivedFrom.Should().Be(SourceName);
                    info.Tool.Should().Be("CodeBrix.Ollama.ModelRunner");
                    info.Settings["type"].Should().Be(type);
                    info.Metadata.Architecture.Should().Be("llama");

                    ResolvedModel resolved = await store.ResolveAsync(outputName, cancellationToken);
                    new FileInfo(resolved.ModelPath).Length.Should().Be(result.OutputBytes);

                    ModelDetails probed = await Runner.ProbeAsync(resolved.ModelPath, cancellationToken);
                    probed.Architecture.Should().Be("llama");
                    probed.VocabularySize.Should().Be(50000);

                    var loadWatch = Stopwatch.StartNew();
                    using IRunningModel model = await Runner.LoadAsync(
                        new ModelRunnerOptions { ModelPath = resolved.ModelPath, ContextSize = 2048 },
                        cancellationToken);
                    loadWatch.Stop();

                    double tokensPerSecond = 0;
                    foreach (string prompt in Prompts)
                    {
                        GenerationResult generated = await model.GenerateToEndAsync(
                            prompt, new GenerationOptions { MaxTokens = 64, Sampling = Greedy() },
                            cancellationToken);

                        generated.FinishReason.Should().BeOneOf(FinishReason.Length, FinishReason.Stop);
                        generated.Statistics.GeneratedTokens.Should().BeGreaterThan(0);
                        LooksLikeAbc(generated.Text).Should().BeTrue(
                            "the continuation of an ABC header should read as ABC, and it was: "
                                + generated.Text);
                        tokensPerSecond = generated.Statistics.TokensPerSecond;
                    }

                    IReadOnlyList<int> quantized = await GreedyAsync(model, Prompts[0], cancellationToken);
                    _output.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: {1} bytes ({2:F1} MiB, {3:P1} of the source) in {4:F1} s; load {5} ms,"
                            + " {6:F1} tokens/s; greedy agreement with the unquantized model {7}/{8}"
                            + " [recorded, not asserted]",
                        type,
                        result.OutputBytes,
                        result.OutputBytes / (1024.0 * 1024.0),
                        (double)result.OutputBytes / result.SourceBytes,
                        stopwatch.Elapsed.TotalSeconds,
                        loadWatch.ElapsedMilliseconds,
                        tokensPerSecond,
                        Agreeing(quantized, reference),
                        GreedyTokens));
                }
                finally
                {
                    await EndToEndStore.RemoveAsync(store, outputName, cancellationToken);
                }
            }
        }
        finally
        {
            await EndToEndStore.RemoveAsync(store, SourceName, cancellationToken);
        }
    }

    /// <summary>
    /// Every type the runner writes is byte for byte what the engine's own command-line quantizer writes for
    /// the same file.
    /// </summary>
    /// <remarks>
    /// The tool is BUILT by a maintainer out of a checkout at the vendored commit, with the shipped native's
    /// own compiler options; MAINTAINER-README records the commands. It can never be checked in and this
    /// repository never reaches for it, so the gate names its path rather than carrying a "1".
    /// </remarks>
    [EnvPathGatedFact(TestGates.QuantizeTool, new[] { TestGates.RunLiveTests })]
    public async Task the_quantized_file_is_what_the_engines_own_tool_writes()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string tool = TestGates.RequireQuantizeTool();
        File.Exists(tool).Should().BeTrue(
            "the path " + TestGates.QuantizeTool + " names must be the engine's built quantizer");

        using ModelStore store = EndToEndStore.Open();
        await EnsureSourceAsync(store, cancellationToken);

        try
        {
            ResolvedModel source = await store.ResolveAsync(SourceName, cancellationToken);
            using var work = new TempWorkDirectory();

            foreach (GgufQuantizationType type in ComparedTypes)
            {
                string ours = work.Combine("ours-" + type + ".gguf");
                string theirs = work.Combine("theirs-" + type + ".gguf");

                //Act
                var stopwatch = Stopwatch.StartNew();
                QuantizeResult result = await Runner.QuantizeAsync(
                    source.ModelPath, ours, type, null, cancellationToken);
                stopwatch.Stop();

                ChildProcessRun run = ChildProcess.Run(
                    tool, new[] { source.ModelPath, theirs, type.ToString() }, null, work.RootPath,
                    cancellationToken);

                //Assert
                run.Exited.Should().BeTrue("the engine's own quantizer must finish" + run.Report());
                run.ExitCode.Should().Be(0, "the engine's own quantizer must succeed" + run.Report());

                long differing = CountDiffering(ours, theirs);
                new FileInfo(theirs).Length.Should().Be(result.OutputBytes);
                differing.Should().Be(0, "the quantized file should be the engine's own bytes");

                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1} bytes, {2} differing, ours in {3:F2} s",
                    type, result.OutputBytes, differing, stopwatch.Elapsed.TotalSeconds));

                File.Delete(ours);
                File.Delete(theirs);
            }
        }
        finally
        {
            await EndToEndStore.RemoveAsync(store, SourceName, cancellationToken);
        }
    }

    /// <summary>
    /// Makes sure the unquantized GGUF model these tests quantize is in the store, converting the checkpoint
    /// if it is not.
    /// </summary>
    /// <param name="store">The store to work in.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>A task that completes when the model is there.</returns>
    private static async Task EnsureSourceAsync(ModelStore store, CancellationToken cancellationToken)
    {
        if (await store.ExistsAsync(SourceName, cancellationToken))
        {
            return;
        }

        string checkpoint = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        await store.ConvertToGgufAsync(
            checkpoint,
            new ConvertOptions
            {
                OutputName = SourceName,
                Overwrite = true,
                AddedSpecialTokens = MuPtSpecialTokens
            },
            null,
            cancellationToken);
    }

    /// <summary>
    /// Loads a stored model and greedily continues the first prompt with it.
    /// </summary>
    /// <param name="store">The store holding it.</param>
    /// <param name="name">The model's name.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>The identifiers it produced.</returns>
    private static async Task<IReadOnlyList<int>> GreedyOfAsync(
        ModelStore store, string name, CancellationToken cancellationToken)
    {
        ResolvedModel resolved = await store.ResolveAsync(name, cancellationToken);
        using IRunningModel model = await Runner.LoadAsync(
            new ModelRunnerOptions { ModelPath = resolved.ModelPath, ContextSize = 2048 }, cancellationToken);
        return await GreedyAsync(model, Prompts[0], cancellationToken);
    }

    /// <summary>
    /// Greedily continues one prompt for the fixed number of tokens and collects what came out.
    /// </summary>
    /// <param name="model">The loaded model.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>The identifiers.</returns>
    private static async Task<IReadOnlyList<int>> GreedyAsync(
        IRunningModel model, string prompt, CancellationToken cancellationToken)
    {
        var ids = new List<int>();
        await foreach (GenerationUpdate update in model.GenerateAsync(
            prompt, new GenerationOptions { MaxTokens = GreedyTokens, Sampling = Greedy() }, cancellationToken))
        {
            ids.AddRange(update.Tokens);
        }

        return ids;
    }

    /// <summary>
    /// The runner's type for one of the tags this test quantizes to.
    /// </summary>
    /// <param name="tag">The tag, as the store spells it.</param>
    /// <returns>The type.</returns>
    /// <remarks>
    /// The store never knows what a tag means - that is the whole point of the seam - so the CONSUMER maps
    /// the two, which is what this method is a miniature of.
    /// </remarks>
    private static GgufQuantizationType TypeOf(string tag)
        => Enum.Parse<GgufQuantizationType>(tag, true);

    /// <summary>
    /// How many bytes of two files differ, counting a difference in length as every byte past the shorter.
    /// </summary>
    /// <param name="left">One file's path.</param>
    /// <param name="right">The other's.</param>
    /// <returns>The number of differing bytes.</returns>
    private static long CountDiffering(string left, string right)
    {
        using FileStream first = File.OpenRead(left);
        using FileStream second = File.OpenRead(right);
        byte[] a = new byte[1 << 20];
        byte[] b = new byte[1 << 20];
        long differing = Math.Abs(first.Length - second.Length);

        while (true)
        {
            int read = first.ReadAtLeast(a, a.Length, false);
            int other = second.ReadAtLeast(b, b.Length, false);
            int shared = Math.Min(read, other);
            for (int i = 0; i < shared; i++)
            {
                if (a[i] != b[i])
                {
                    differing++;
                }
            }

            if (shared == 0)
            {
                return differing;
            }
        }
    }

    /// <summary>
    /// How many identifiers of two continuations agree, counted from the beginning.
    /// </summary>
    /// <param name="produced">What the quantized model produced.</param>
    /// <param name="reference">What the model it was quantized from produced.</param>
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
    /// Whether text reads as the ABC notation this model writes.
    /// </summary>
    /// <param name="text">What was generated.</param>
    /// <returns><see langword="true"/> when it carries any of the notation's marks.</returns>
    private static bool LooksLikeAbc(string text)
        => text.Contains("<|>", StringComparison.Ordinal)
            || text.Contains('|')
            || text.Contains("V:", StringComparison.Ordinal)
            || text.Contains("K:", StringComparison.Ordinal)
            || text.Contains("L:", StringComparison.Ordinal);

    /// <summary>
    /// Sampling that is not sampling: temperature zero, and every truncation and penalty neutralised.
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
}

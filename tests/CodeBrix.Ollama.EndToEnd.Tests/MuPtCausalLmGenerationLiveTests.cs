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

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The whole text-generation route, end to end and against the people who wrote the model: the store exports
/// a checkpoint to an ONNX bundle and reduces it, the runner is handed the files BY PATH, and what it writes
/// is compared with what the bundle's own runtime writes from the same prompts.
/// </summary>
/// <remarks>
/// <para>
/// R7'S CONSUMER PATTERN IS WHAT CONNECTS THE TWO LIBRARIES HERE, and nothing else does: the store exports
/// and reduces a bundle it holds, <c>MaterializeAsync</c> lays the files out by hard links that cost nothing,
/// and the runner is given the (logical name to path) pairs. Neither library names a type of the other.
/// </para>
/// <para>
/// WHAT IS ASSERTED AND WHAT IS RECORDED follows the plan's tolerance classes. The full-precision export must
/// produce the SAME TOKEN NUMBERS as the oracle, token for token - that is the phase's done criterion.
/// A variant that quantizes only its WEIGHTS must agree at every position under teacher forcing. A variant
/// that quantizes its ACTIVATIONS - the builder's four-bit export, which asks a runtime to do that through
/// its accuracy_level attribute, and the dynamic eight-bit rewrite, which does it by construction - has its
/// agreement RECORDED, because holding two engines closer than one of them agrees with itself would be
/// pinning noise.
/// </para>
/// <para>
/// THE PROMPTS ARE THE TEST'S, NOT THE LIBRARY'S. This family's cards write a line break as
/// <c>&lt;n&gt;</c>, and that is a fact about one publisher's model: it lives in <see cref="MuPtFacts"/> on
/// this side of the fence. The driver knows nothing about it and neither does anything under src/.
/// </para>
/// </remarks>
public sealed class MuPtCausalLmGenerationLiveTests
{
    /// <summary>How many tokens each prompt is continued by.</summary>
    private const int Tokens = 32;

    /// <summary>How many threads both engines are given for the comparisons.</summary>
    private const int Threads = 8;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class.</summary>
    /// <param name="output">Where the measured numbers are written.</param>
    public MuPtCausalLmGenerationLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// DONE CRITERION. The full-precision export writes exactly what the bundle's own runtime writes, from
    /// the same prompts, token for token - and the tokenizer that read those prompts gave exactly the token
    /// numbers the publisher's own Python gives.
    /// </summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task GenerateAsync_writes_what_the_venv_writes_on_the_full_precision_export()
    {
        //Arrange
        using ModelStore store = EndToEndStore.Open();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TempWorkDirectory work = new TempWorkDirectory("mupt-text-fp32");
        IReadOnlyDictionary<string, string> files = await LayOutAsync(
            store, "fp32", "onnx-oracle/mupt-190m:fp32", null, work, cancellationToken);

        //Act
        Generated managed = await GenerateAsync(files, cancellationToken);
        CausalLmOracleRun oracle = CausalLmOracle.Run(
            TestGates.RequireVirtualEnvironment(), work.DirectoryPath, work.Combine("oracle"),
            MuPtFacts.Prompts, Tokens, Threads, managed.Sequences, cancellationToken);

        //Assert
        _output.WriteLine("oracle: " + oracle.Engine);
        oracle.Prompts.Should().HaveCount(MuPtFacts.Prompts.Length);

        for (int i = 0; i < MuPtFacts.Prompts.Length; i++)
        {
            managed.Prompts[i].Should().Equal(
                oracle.Prompts[i].Ids, "the prompt's token numbers must be the publisher's own");
            managed.Tokens[i].Should().Equal(
                oracle.Prompts[i].Generated, "greedy generation must choose the same token every time");
            managed.Tokens[i].Should().HaveCount(Tokens);

            _output.WriteLine(
                "prompt " + i.ToString(CultureInfo.InvariantCulture) + ": "
                + managed.Prompts[i].Count.ToString(CultureInfo.InvariantCulture) + " prompt tokens, "
                + managed.Tokens[i].Count.ToString(CultureInfo.InvariantCulture) + " generated, identical");
            _output.WriteLine("  " + Readable(managed.Text[i]));
        }

        //It writes the notation the model was trained on - checked loosely on purpose, because pinning
        //generated text would be pinning the model rather than this library.
        string all = string.Concat(managed.Text);
        all.Should().NotBeEmpty();
        all.Trim().Should().NotBeEmpty();

        //And the whole-precision teacher forcing agrees with itself, which is the baseline every quantized
        //comparison below is measured against.
        IReadOnlyList<IReadOnlyList<int>> forced = await CausalLmTeacherForcing.RunAsync(
            files, managed.Sequences, Threads, cancellationToken);
        (int differing, int compared) = CausalLmTeacherForcing.Compare(forced, oracle.TeacherForced);
        _output.WriteLine(
            "teacher forcing, full precision: " + differing.ToString(CultureInfo.InvariantCulture)
            + " of " + compared.ToString(CultureInfo.InvariantCulture) + " positions differ");
        differing.Should().Be(0);
    }

    /// <summary>
    /// The builder's four-bit export. Its agreement is RECORDED rather than asserted: it carries
    /// accuracy_level on every quantized matrix multiply, which asks a runtime to quantize the activations as
    /// well, and this engine keeps floats - so the two are doing different arithmetic by the graph's own
    /// request.
    /// </summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task GenerateAsync_agreement_with_the_venv_on_the_four_bit_export_is_recorded() =>
        await CompareAsync(
            "int4", "onnx-oracle/mupt-190m:int4", null, "the builder's four-bit export", false);

    /// <summary>
    /// The store's four-bit reduction, which quantizes WEIGHTS only. Every position under teacher forcing
    /// must agree.
    /// </summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task GenerateAsync_matches_the_venv_under_teacher_forcing_on_the_four_bit_reduction() =>
        await CompareAsync(
            "fp32", "onnx-oracle/mupt-190m:fp32-int4", ReduceMode.WeightOnlyInt4,
            "the store's four-bit reduction", true);

    /// <summary>
    /// The store's eight-bit reduction, which quantizes WEIGHTS only. Every position under teacher forcing
    /// must agree.
    /// </summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task GenerateAsync_matches_the_venv_under_teacher_forcing_on_the_eight_bit_reduction() =>
        await CompareAsync(
            "fp32", "onnx-oracle/mupt-190m:fp32-int8", ReduceMode.WeightOnlyInt8,
            "the store's eight-bit reduction", true);

    /// <summary>
    /// The store's dynamic eight-bit rewrite, which quantizes ACTIVATIONS by construction. Its agreement is
    /// RECORDED: one last-bit difference moves a whole tensor's scale, and this engine's own two arithmetic
    /// paths disagree with each other on such graphs.
    /// </summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task GenerateAsync_agreement_with_the_venv_on_the_dynamic_eight_bit_reduction_is_recorded() =>
        await CompareAsync(
            "fp32", "onnx-oracle/mupt-190m:fp32-dynamic-int8", ReduceMode.DynamicInt8,
            "the store's dynamic eight-bit rewrite", false);

    /// <summary>
    /// The numbers: tokens a second at one thread and at eight, for the full-precision export and its
    /// four-bit form, with the time to the first token and the peak resident memory.
    /// </summary>
    /// <returns>A task that completes when the measurement is done.</returns>
    [EnvGatedFact(new[] { TestGates.RunLiveTests })]
    public async Task GenerateAsync_measures_tokens_a_second()
    {
        //Arrange
        using ModelStore store = EndToEndStore.Open();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        (string Label, string Precision, ReduceMode? Reduction)[] subjects =
        {
            ("fp32", "fp32", null),
            ("the builder's int4", "int4", null),
            ("the store's int4", "fp32", ReduceMode.WeightOnlyInt4),
        };

        //Act and assert
        foreach ((string label, string precision, ReduceMode? reduction) in subjects)
        {
            using TempWorkDirectory work = new TempWorkDirectory("mupt-text-numbers");
            IReadOnlyDictionary<string, string> files = await LayOutAsync(
                store, precision,
                reduction.HasValue ? "onnx-oracle/mupt-190m:fp32-int4" : "onnx-oracle/mupt-190m:" + precision,
                reduction, work, cancellationToken);

            foreach (int threads in new[] { 1, 8 })
            {
                ProcessMemory.ResetPeak();
                Stopwatch load = Stopwatch.StartNew();
                await using IOnnxCausalLmModel model = await OnnxCausalLmModel.LoadFromFilesAsync(
                    files, new OnnxRunnerOptions { Threads = threads }, cancellationToken);
                load.Stop();

                Stopwatch first = Stopwatch.StartNew();
                bool seen = false;
                int generated = 0;
                GenerationStatistics statistics = null;
                TimeSpan toFirstToken = TimeSpan.Zero;

                await foreach (GenerationUpdate update in model.GenerateAsync(
                    MuPtFacts.Prompts[0], Greedy(64), cancellationToken))
                {
                    if (!seen)
                    {
                        toFirstToken = first.Elapsed;
                        seen = true;
                    }

                    generated += update.Tokens.Length;
                    if (update.IsFinal) statistics = update.Statistics;
                }

                statistics.Should().NotBeNull();
                generated.Should().BeGreaterThan(0);

                _output.WriteLine(
                    label + ", " + threads.ToString(CultureInfo.InvariantCulture) + " thread(s): "
                    + statistics.TokensPerSecond.ToString("F2", CultureInfo.InvariantCulture)
                    + " tokens/s over " + generated.ToString(CultureInfo.InvariantCulture)
                    + " tokens; prompt ("
                    + statistics.PromptTokens.ToString(CultureInfo.InvariantCulture) + " tokens) "
                    + statistics.PromptDuration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)
                    + " ms; first token " + toFirstToken.TotalMilliseconds.ToString(
                        "F1", CultureInfo.InvariantCulture)
                    + " ms; load " + load.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)
                    + " ms; peak resident " + ProcessMemory.PeakResidentText());
            }
        }
    }

    private static GenerationOptions Greedy(int maximum) => new GenerationOptions
    {
        MaxTokens = maximum,
        Sampling = new SamplingOptions { Temperature = 0f, RepeatPenalty = 1f, RepeatLastN = 0 },
    };

    private static string Readable(string text) =>
        text.Replace("\n", "\\n", StringComparison.Ordinal).Trim();

    private async Task CompareAsync(
        string precision, string name, ReduceMode? reduction, string what, bool assertTeacherForcing)
    {
        //Arrange
        using ModelStore store = EndToEndStore.Open();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TempWorkDirectory work = new TempWorkDirectory("mupt-text");
        IReadOnlyDictionary<string, string> files = await LayOutAsync(
            store, precision, name, reduction, work, cancellationToken);

        //Act
        Generated managed = await GenerateAsync(files, cancellationToken);
        CausalLmOracleRun oracle = CausalLmOracle.Run(
            TestGates.RequireVirtualEnvironment(), work.DirectoryPath, work.Combine("oracle"),
            MuPtFacts.Prompts, Tokens, Threads, managed.Sequences, cancellationToken);
        IReadOnlyList<IReadOnlyList<int>> forced = await CausalLmTeacherForcing.RunAsync(
            files, managed.Sequences, Threads, cancellationToken);

        //Assert
        for (int i = 0; i < MuPtFacts.Prompts.Length; i++)
        {
            managed.Prompts[i].Should().Equal(oracle.Prompts[i].Ids);
            _output.WriteLine(
                what + ", prompt " + i.ToString(CultureInfo.InvariantCulture)
                + ": first difference from the oracle's own generation at "
                + FirstDifference(managed.Tokens[i], oracle.Prompts[i].Generated));
            _output.WriteLine("  " + Readable(managed.Text[i]));
        }

        (int differing, int compared) = CausalLmTeacherForcing.Compare(forced, oracle.TeacherForced);
        _output.WriteLine(
            what + ", teacher forcing: " + differing.ToString(CultureInfo.InvariantCulture) + " of "
            + compared.ToString(CultureInfo.InvariantCulture) + " positions differ");

        compared.Should().BeGreaterThan(0);
        if (assertTeacherForcing)
        {
            differing.Should().Be(
                0, "a variant that quantizes only its weights must choose the same token at every position");
        }
    }

    private static string FirstDifference(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        for (int i = 0; i < left.Count && i < right.Count; i++)
        {
            if (left[i] != right[i]) return "token " + i.ToString(CultureInfo.InvariantCulture);
        }

        return left.Count == right.Count ? "nowhere - they are identical" : "the end of the shorter one";
    }

    private static async Task<IReadOnlyDictionary<string, string>> LayOutAsync(
        ModelStore store,
        string precision,
        string name,
        ReduceMode? reduction,
        TempWorkDirectory work,
        CancellationToken cancellationToken)
    {
        string source = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_190M, cancellationToken);
        string exported = await EndToEndStore.EnsureExportedAsync(
            store, source, precision, "onnx-oracle/mupt-190m:" + precision, cancellationToken);

        string wanted = exported;
        if (reduction.HasValue)
        {
            wanted = await EndToEndStore.EnsureReducedAsync(
                store, exported, reduction.Value, name, cancellationToken);
        }

        //The files are laid out as a DIRECTORY because the oracle's own libraries read a bundle that way.
        //The runner does not need it - it takes the pairs below - but both sides then read identical bytes.
        IReadOnlyList<string> written = await store.MaterializeAsync(
            wanted, work.DirectoryPath, null, cancellationToken);
        written.Should().NotBeEmpty();

        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in written)
        {
            files[Path.GetRelativePath(work.DirectoryPath, path).Replace('\\', '/')] = path;
        }

        return files;
    }

    private static async Task<Generated> GenerateAsync(
        IReadOnlyDictionary<string, string> files, CancellationToken cancellationToken)
    {
        await using IOnnxCausalLmModel model = await OnnxCausalLmModel.LoadFromFilesAsync(
            files, new OnnxRunnerOptions { Threads = Threads }, cancellationToken);

        Generated generated = new Generated();
        foreach (string prompt in MuPtFacts.Prompts)
        {
            IReadOnlyList<int> promptTokens = await model.TokenizeAsync(
                prompt, cancellationToken: cancellationToken);

            List<int> tokens = new List<int>();
            await foreach (GenerationUpdate update in model.GenerateAsync(
                prompt, Greedy(Tokens), cancellationToken))
            {
                tokens.AddRange(update.Tokens);
            }

            List<int> whole = new List<int>(promptTokens);
            whole.AddRange(tokens);

            generated.Prompts.Add(promptTokens);
            generated.Tokens.Add(tokens);
            generated.Sequences.Add(whole);
            generated.Text.Add(await model.DetokenizeAsync(
                tokens, cancellationToken: cancellationToken));
        }

        return generated;
    }

    private sealed class Generated
    {
        public List<IReadOnlyList<int>> Prompts { get; } = new List<IReadOnlyList<int>>();

        public List<IReadOnlyList<int>> Tokens { get; } = new List<IReadOnlyList<int>>();

        public List<IReadOnlyList<int>> Sequences { get; } = new List<IReadOnlyList<int>>();

        public List<string> Text { get; } = new List<string>();
    }
}

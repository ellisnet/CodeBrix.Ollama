using System;
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
/// The streaming proof: the largest checkpoint of the family - one PyTorch zip pickle of nearly four gigabytes
/// - is converted to GGUF and run, and what the conversion COSTS is measured rather than assumed.
/// </summary>
/// <remarks>
/// <para>
/// The number this test exists for is the PEAK RESIDENT SET of the process that converts. A conversion reads
/// the checkpoint one tensor at a time and writes each tensor as it is read, so the memory it needs is a
/// function of the largest single tensor and not of the file: if that number tracked the file size instead,
/// every claim made about the design would be wrong. It is read from the operating system's own high-water
/// mark rather than from a sample, so a peak between two samples cannot be missed.
/// </para>
/// <para>
/// TWO GATES, and the second one is never opened by accident: the live gate, and the same large-repository
/// gate the store's own suite uses. Opening both downloads about 3.9 GB, converts it into another 4 GB through
/// the system temporary directory, and KEEPS the checkpoint in the test-model cache afterwards so that a second
/// run costs nothing. Point TMPDIR at a real file system first; a temporary directory in memory cannot hold it.
/// </para>
/// </remarks>
public sealed class MuPtLargeGgufLiveTests
{
    /// <summary>How many tokens the throughput measurement generates.</summary>
    private const int GeneratedTokens = 64;

    /// <summary>What the file the conversion reads is, so the peak can be stated as a fraction of it.</summary>
    private const long CheckpointBytes = 3931517782L;

    /// <summary>Where the sizes, durations and the peak resident set are written.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Takes the output the measurements go to.
    /// </summary>
    /// <param name="output">Where the run reports what it did.</param>
    public MuPtLargeGgufLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [EnvGatedFact(new[] { TestGates.RunLiveTests, TestGates.RunLargeMusicTests })]
    public async Task the_largest_checkpoint_converts_without_being_held_in_memory()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = EndToEndStore.Open();
        var pullWatch = Stopwatch.StartNew();
        string sourceName = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.MuPtV1_1_97B, cancellationToken);
        pullWatch.Stop();
        _output.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "pull or reuse: {0:F1} s", pullWatch.Elapsed.TotalSeconds));

        const string outputName = "hf.co/m-a-p/MuPT-v1-8192-1.97B:gguf-endtoend";
        await EndToEndStore.RemoveAsync(store, outputName, cancellationToken);
        long peakBefore = PeakResidentBytes();

        //Act
        var convertWatch = Stopwatch.StartNew();
        ConvertResult result = await store.ConvertToGgufAsync(
            sourceName,
            new ConvertOptions
            {
                OutputName = outputName,
                OutputType = GgufOutputType.Auto,
                Overwrite = true,
                AddedSpecialTokens = MuPtFacts.SpecialTokens
            },
            null,
            cancellationToken);
        convertWatch.Stop();
        long peakAfter = PeakResidentBytes();

        try
        {
            //Assert
            result.Architecture.Should().Be(CheckpointArchitecture.Llama);
            result.TypeWritten.Should().Be(GgufOutputType.BF16);
            result.TensorCount.Should().Be(435);
            result.SourceBytes.Should().Be(CheckpointBytes);
            result.OutputBytes.Should().BeGreaterThan(CheckpointBytes);

            _output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "converted {0} bytes ({1:F2} GiB) to {2} bytes ({3:F2} GiB), {4} tensors, in {5:F1} s",
                result.SourceBytes, result.SourceBytes / (1024.0 * 1024 * 1024),
                result.OutputBytes, result.OutputBytes / (1024.0 * 1024 * 1024),
                result.TensorCount, convertWatch.Elapsed.TotalSeconds));
            _output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "peak resident set of the converting process: {0} before, {1} after ({2:F1} MiB), which is "
                + "{3:F1}% of the checkpoint",
                peakBefore, peakAfter, peakAfter / (1024.0 * 1024), 100.0 * peakAfter / CheckpointBytes));

            //The point of the exercise. The conversion streams, so its high-water mark is a function of the
            //largest single tensor - here the 50,000 x 1,536 embedding matrix - and not of the file. A quarter
            //of the checkpoint is a generous ceiling: anything near the file size would mean it was held.
            if (peakAfter > 0)
            {
                peakAfter.Should().BeLessThan(CheckpointBytes / 4,
                    "a streaming conversion must not grow with the size of the checkpoint");
            }

            ModelInfo info = await store.ShowAsync(outputName, cancellationToken);
            info.Format.Should().Be("gguf");
            info.Metadata.Architecture.Should().Be("llama");

            ResolvedModel resolved = await store.ResolveAsync(outputName, cancellationToken);
            new FileInfo(resolved.ModelPath).Length.Should().Be(result.OutputBytes);

            ModelDetails probed = await Runner.ProbeAsync(resolved.ModelPath, cancellationToken);
            probed.Architecture.Should().Be("llama");
            probed.VocabularySize.Should().Be(50000);

            var loadWatch = Stopwatch.StartNew();
            using IRunningModel model = await Runner.LoadAsync(
                new ModelRunnerOptions { ModelPath = resolved.ModelPath, ContextSize = 2048 }, cancellationToken);
            loadWatch.Stop();

            GenerationResult generated = await model.GenerateToEndAsync(
                MuPtFacts.Prompts[0],
                new GenerationOptions { MaxTokens = GeneratedTokens, Sampling = Greedy() },
                cancellationToken);

            generated.FinishReason.Should().BeOneOf(FinishReason.Length, FinishReason.Stop);
            generated.Statistics.GeneratedTokens.Should().BeGreaterThan(0);
            generated.Text.Should().NotBeNullOrWhiteSpace();

            _output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "load {0} ms; {1} tokens at {2:F1} tokens/s",
                loadWatch.ElapsedMilliseconds, generated.Statistics.GeneratedTokens,
                generated.Statistics.TokensPerSecond));
            _output.WriteLine("continuation: " + Shorten(generated.Text));
            _output.WriteLine("test-model cache: " + EndToEndStore.ResolveDirectory());
        }
        finally
        {
            //The converted file is another four gigabytes and is cheap to make again; the CHECKPOINT stays,
            //because downloading it again is what costs.
            await EndToEndStore.RemoveAsync(store, outputName, cancellationToken);
        }
    }

    /// <summary>
    /// The high-water mark of this process's resident set, in bytes.
    /// </summary>
    /// <returns>The peak, or zero on a system that does not publish one.</returns>
    /// <remarks>
    /// Linux keeps the high-water mark itself, as <c>VmHWM</c> in <c>/proc/self/status</c>, which is why this
    /// reads a file rather than sampling: a peak that happened between two samples would never be seen. On a
    /// system with no such file the measurement is reported as unavailable and the assertion is skipped rather
    /// than guessed at.
    /// </remarks>
    private static long PeakResidentBytes()
    {
        const string path = "/proc/self/status";
        try
        {
            if (!File.Exists(path))
            {
                return 0;
            }

            foreach (string line in File.ReadAllLines(path))
            {
                if (!line.StartsWith("VmHWM:", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out long kilobytes))
                {
                    return kilobytes * 1024;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return 0;
    }

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

    /// <summary>
    /// The first part of a generated text, for the run's own output.
    /// </summary>
    /// <param name="text">What was generated.</param>
    /// <returns>At most 160 characters of it.</returns>
    private static string Shorten(string text)
        => text.Length <= 160 ? text : text.Substring(0, 160) + "...";
}

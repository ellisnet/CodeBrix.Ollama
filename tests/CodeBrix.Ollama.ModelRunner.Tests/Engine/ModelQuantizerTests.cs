using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers <see cref="ModelRunner.QuantizeAsync"/>: the bytes it writes, the arguments it refuses and the file
/// it is careful not to leave behind.
/// </summary>
/// <remarks>
/// <para>
/// THE CORRECTNESS GATE IS THE ENGINE'S OWN TOOL. Fixtures/Quantize holds five quantizations of the tiny
/// conformance model written by the inference engine's own command-line quantizer, built out of a checkout at
/// the vendored commit with the shipped native's own compiler options; Fixtures/Quantize/README.txt records
/// exactly how. The comparison here is byte for byte over the whole file, which is the only comparison worth
/// making: a quantizer that rounded one block differently would still produce a file of the right size with
/// the right keys and the right tensor types.
/// </para>
/// <para>
/// The subject is the repository's own conformance model - random weights from a fixed seed, 21 F32 tensors,
/// no vocabulary - so nothing here is downloaded, nothing outside the repository is read, and the outputs go
/// into a folder inside the test assembly's own output directory.
/// </para>
/// </remarks>
public sealed class ModelQuantizerTests
{
    /// <summary>Where the oracle files sit beside the test assembly.</summary>
    private static string OracleDirectory
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Quantize");

    /// <summary>The model every test quantizes.</summary>
    private static string Source => TestVectors.ConformanceModelPath;

    /// <summary>The file the engine's own tool wrote for one type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The path of the checked-in oracle.</returns>
    private static string OraclePath(GgufQuantizationType type)
        => Path.Combine(OracleDirectory, "conformance-tiny-" + type + ".gguf");

    /// <summary>What the engine's own tool writes is what this library writes, byte for byte.</summary>
    /// <param name="type">The type to write.</param>
    [Theory]
    [InlineData(GgufQuantizationType.Q8_0)]
    [InlineData(GgufQuantizationType.Q4_0)]
    [InlineData(GgufQuantizationType.Q4_K_M)]
    [InlineData(GgufQuantizationType.Q5_K_M)]
    [InlineData(GgufQuantizationType.Q6_K)]
    public async Task QuantizeAsync_writes_what_the_engines_own_tool_writes(GgufQuantizationType type)
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string output = scratch.Combine("quantized-" + type + ".gguf");
        byte[] expected = await File.ReadAllBytesAsync(OraclePath(type), TestContext.Current.CancellationToken);

        //Act
        QuantizeResult result = await ModelRunner.QuantizeAsync(
            Source, output, type, null, TestContext.Current.CancellationToken);

        //Assert
        byte[] written = await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken);
        written.Length.Should().Be(expected.Length);
        CountDiffering(written, expected).Should().Be(0);
        result.OutputBytes.Should().Be(expected.Length);
    }

    /// <summary>The result says what was read, what was written and what type it is.</summary>
    [Fact]
    public async Task QuantizeAsync_reports_what_it_read_and_wrote()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string output = scratch.Combine("reported.gguf");

        //Act
        QuantizeResult result = await ModelRunner.QuantizeAsync(
            Source, output, GgufQuantizationType.Q8_0, null, TestContext.Current.CancellationToken);

        //Assert
        result.InputPath.Should().Be(Path.GetFullPath(Source));
        result.OutputPath.Should().Be(Path.GetFullPath(output));
        result.Type.Should().Be(GgufQuantizationType.Q8_0);
        result.InputBytes.Should().Be(new FileInfo(Source).Length);
        result.OutputBytes.Should().Be(new FileInfo(output).Length);
        (result.OutputBytes < result.InputBytes).Should().BeTrue();
        (result.Elapsed > TimeSpan.Zero).Should().BeTrue();
    }

    /// <summary>
    /// The options type's own defaults are the values the engine calls its defaults, which is what makes a
    /// call with no options the call the engine's tool makes with no switches.
    /// </summary>
    [Fact]
    public unsafe void The_option_defaults_are_the_engines_own_defaults()
    {
        //Arrange
        var options = new QuantizeOptions();
        LlamaModelQuantizeParams engine = NativeDefaults.QuantizeParams;

        //Assert
        options.Threads.Should().Be(engine.NThread);
        options.AllowRequantize.Should().Be(engine.AllowRequantize != 0);
        options.Pure.Should().Be(engine.Pure != 0);
    }

    /// <summary>Asking with default options writes exactly what asking with none writes.</summary>
    [Fact]
    public async Task QuantizeAsync_with_default_options_writes_what_no_options_writes()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string withNone = scratch.Combine("none.gguf");
        string withDefaults = scratch.Combine("defaults.gguf");

        //Act
        await ModelRunner.QuantizeAsync(Source, withNone, GgufQuantizationType.Q4_K_M, null,
            TestContext.Current.CancellationToken);
        await ModelRunner.QuantizeAsync(Source, withDefaults, GgufQuantizationType.Q4_K_M,
            new QuantizeOptions(), TestContext.Current.CancellationToken);

        //Assert
        byte[] none = await File.ReadAllBytesAsync(withNone, TestContext.Current.CancellationToken);
        byte[] defaults = await File.ReadAllBytesAsync(withDefaults, TestContext.Current.CancellationToken);
        defaults.Length.Should().Be(none.Length);
        CountDiffering(defaults, none).Should().Be(0);
    }

    /// <summary>The thread count changes how long the work takes and never what it writes.</summary>
    [Fact]
    public async Task QuantizeAsync_writes_the_same_bytes_however_many_threads_it_is_given()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string oneThread = scratch.Combine("one.gguf");

        //Act
        await ModelRunner.QuantizeAsync(Source, oneThread, GgufQuantizationType.Q6_K,
            new QuantizeOptions { Threads = 1 }, TestContext.Current.CancellationToken);

        //Assert
        byte[] written = await File.ReadAllBytesAsync(oneThread, TestContext.Current.CancellationToken);
        byte[] expected = await File.ReadAllBytesAsync(
            OraclePath(GgufQuantizationType.Q6_K), TestContext.Current.CancellationToken);
        CountDiffering(written, expected).Should().Be(0);
    }

    /// <summary>The quantized file is a model the engine reads back.</summary>
    [Fact]
    public async Task QuantizeAsync_writes_a_file_the_engine_reads_back()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string output = scratch.Combine("readback.gguf");
        await ModelRunner.QuantizeAsync(Source, output, GgufQuantizationType.Q8_0, null,
            TestContext.Current.CancellationToken);

        //Act
        ModelDetails details = await ModelRunner.ProbeAsync(output, TestContext.Current.CancellationToken);

        //Assert
        details.Architecture.Should().Be("llama");
        details.VocabularySize.Should().Be(64);
        details.Description.Should().Contain("Q8_0");
    }

    /// <summary>A path that is not set is refused before anything is opened.</summary>
    /// <param name="input">The input path to try.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task QuantizeAsync_with_no_input_path_throws(string input)
    {
        //Arrange
        Func<Task> act = () => ModelRunner.QuantizeAsync(input, "out.gguf", GgufQuantizationType.Q8_0, null,
            TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("inputPath");
    }

    /// <summary>An output path that is not set is refused before anything is opened.</summary>
    /// <param name="output">The output path to try.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task QuantizeAsync_with_no_output_path_throws(string output)
    {
        //Arrange
        Func<Task> act = () => ModelRunner.QuantizeAsync(Source, output, GgufQuantizationType.Q8_0, null,
            TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("outputPath");
    }

    /// <summary>A type the engine has no mapping for is refused by its number.</summary>
    [Fact]
    public async Task QuantizeAsync_with_a_type_the_engine_does_not_write_throws()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        Func<Task> act = () => ModelRunner.QuantizeAsync(Source, scratch.Combine("never.gguf"),
            (GgufQuantizationType)999, null, TestContext.Current.CancellationToken);

        //Act
        ArgumentOutOfRangeException thrown =
            (await act.Should().ThrowAsync<ArgumentOutOfRangeException>()).Which;

        //Assert
        thrown.ParamName.Should().Be("type");
        thrown.Message.Should().Contain("999");
    }

    /// <summary>A quantization will not write over the file it is reading.</summary>
    [Fact]
    public async Task QuantizeAsync_refuses_to_write_over_the_file_it_reads()
    {
        //Arrange
        Func<Task> act = () => ModelRunner.QuantizeAsync(Source, Source, GgufQuantizationType.Q8_0, null,
            TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("outputPath");
    }

    /// <summary>A source that is not there is named as the file that is missing.</summary>
    [Fact]
    public async Task QuantizeAsync_with_a_source_that_is_not_there_throws()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string missing = scratch.Combine("nothing-here.gguf");
        Func<Task> act = () => ModelRunner.QuantizeAsync(missing, scratch.Combine("out.gguf"),
            GgufQuantizationType.Q8_0, null, TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<FileNotFoundException>()).Which.Message.Should()
            .Contain(Path.GetFullPath(missing));
    }

    /// <summary>An output directory that does not exist is named rather than created.</summary>
    [Fact]
    public async Task QuantizeAsync_with_no_output_directory_throws()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string output = Path.Combine(scratch.DirectoryPath, "no-such-folder", "out.gguf");
        Func<Task> act = () => ModelRunner.QuantizeAsync(Source, output, GgufQuantizationType.Q8_0, null,
            TestContext.Current.CancellationToken);

        //Act and assert
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    /// <summary>A source the engine will not read leaves nothing at the output path.</summary>
    [Fact]
    public async Task QuantizeAsync_leaves_nothing_behind_when_the_engine_refuses()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string notAModel = scratch.Combine("not-a-model.gguf");
        await File.WriteAllTextAsync(notAModel, "this is not a GGUF file",
            TestContext.Current.CancellationToken);
        string output = scratch.Combine("refused.gguf");
        Func<Task> act = () => ModelRunner.QuantizeAsync(notAModel, output, GgufQuantizationType.Q8_0, null,
            TestContext.Current.CancellationToken);

        //Act
        ModelLoadException thrown = (await act.Should().ThrowAsync<ModelLoadException>()).Which;

        //Assert
        thrown.Message.Should().Contain(Path.GetFullPath(notAModel));
        File.Exists(output).Should().BeFalse();
        Directory.GetFiles(scratch.DirectoryPath).Should().HaveCount(1);
    }

    /// <summary>A failure leaves a file that was already at the output path exactly as it was.</summary>
    [Fact]
    public async Task QuantizeAsync_leaves_an_existing_output_untouched_when_the_engine_refuses()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string notAModel = scratch.Combine("not-a-model.gguf");
        await File.WriteAllTextAsync(notAModel, "this is not a GGUF file",
            TestContext.Current.CancellationToken);
        string output = scratch.Combine("precious.gguf");
        await File.WriteAllTextAsync(output, "do not lose me", TestContext.Current.CancellationToken);
        Func<Task> act = () => ModelRunner.QuantizeAsync(notAModel, output, GgufQuantizationType.Q8_0, null,
            TestContext.Current.CancellationToken);

        //Act
        await act.Should().ThrowAsync<ModelLoadException>();

        //Assert
        (await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken)).Should()
            .Be("do not lose me");
    }

    /// <summary>A successful quantization replaces a file already at the output path.</summary>
    [Fact]
    public async Task QuantizeAsync_replaces_a_file_already_at_the_output_path()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string output = scratch.Combine("replaced.gguf");
        await File.WriteAllTextAsync(output, "the older one", TestContext.Current.CancellationToken);

        //Act
        QuantizeResult result = await ModelRunner.QuantizeAsync(Source, output, GgufQuantizationType.Q4_0,
            null, TestContext.Current.CancellationToken);

        //Assert
        byte[] written = await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken);
        byte[] expected = await File.ReadAllBytesAsync(
            OraclePath(GgufQuantizationType.Q4_0), TestContext.Current.CancellationToken);
        CountDiffering(written, expected).Should().Be(0);
        result.OutputBytes.Should().Be(expected.Length);
    }

    /// <summary>A token that is already cancelled stops the call before the engine is asked.</summary>
    [Fact]
    public async Task QuantizeAsync_with_a_cancelled_token_does_not_start()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        string output = scratch.Combine("cancelled.gguf");
        Func<Task> act = () => ModelRunner.QuantizeAsync(Source, output, GgufQuantizationType.Q8_0, null,
            source.Token);

        //Act
        await act.Should().ThrowAsync<OperationCanceledException>();

        //Assert
        File.Exists(output).Should().BeFalse();
        Directory.GetFiles(scratch.DirectoryPath).Should().BeEmpty();
    }

    /// <summary>
    /// A source that is already quantized is refused unless the caller says it may be quantized again.
    /// </summary>
    [Fact]
    public async Task QuantizeAsync_refuses_an_already_quantized_source_unless_it_is_allowed()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        string once = scratch.Combine("once.gguf");
        await ModelRunner.QuantizeAsync(Source, once, GgufQuantizationType.Q8_0, null,
            TestContext.Current.CancellationToken);
        string twice = scratch.Combine("twice.gguf");
        Func<Task> act = () => ModelRunner.QuantizeAsync(once, twice, GgufQuantizationType.Q4_0, null,
            TestContext.Current.CancellationToken);

        //Act
        await act.Should().ThrowAsync<ModelLoadException>();

        //Assert
        File.Exists(twice).Should().BeFalse();
        QuantizeResult allowed = await ModelRunner.QuantizeAsync(once, twice, GgufQuantizationType.Q4_0,
            new QuantizeOptions { AllowRequantize = true }, TestContext.Current.CancellationToken);
        allowed.OutputBytes.Should().Be(new FileInfo(twice).Length);
    }

    /// <summary>
    /// How many bytes of two arrays differ, counting a difference in length as every byte past the shorter.
    /// </summary>
    /// <param name="left">One array.</param>
    /// <param name="right">The other.</param>
    /// <returns>The number of differing bytes.</returns>
    private static int CountDiffering(byte[] left, byte[] right)
    {
        int shared = Math.Min(left.Length, right.Length);
        int differing = Math.Abs(left.Length - right.Length);
        for (int i = 0; i < shared; i++)
        {
            if (left[i] != right[i])
            {
                differing++;
            }
        }

        return differing;
    }
}

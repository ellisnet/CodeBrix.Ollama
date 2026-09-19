using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers quantizing a stored GGUF model: the name the result takes, the provenance it records, what the
/// caller's quantizer is handed, what is carried over from the source, the progress it reports, what it
/// refuses, and that the working folder never outlives the call.
/// </summary>
/// <remarks>
/// <para>
/// THE QUANTIZER HERE IS A FAKE, AND THAT IS THE POINT. This library has no quantizer: the real one lives in
/// the package that carries an inference engine, which this package does not reference and never will. So
/// these tests supply a delegate that copies or rewrites bytes, which is all the store needs to be able to
/// say the whole of what it does. Whether a real quantizer writes the right weights is the runner suite's
/// question, and it answers it against the engine's own tool.
/// </para>
/// <para>
/// Nothing here needs a network, an interpreter, a native library or a model file from anybody else.
/// </para>
/// </remarks>
public sealed class ModelStoreQuantizeTests
{
    private const string SourceName = "local/fixtures/quantizable:latest";
    private const string BundleName = "local/fixtures/bundle-source:latest";
    private const string ShardedName = "local/fixtures/sharded:latest";

    [Fact]
    public async Task QuantizeGgufAsync_stores_the_result_under_the_type_tag()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);

        //Act
        QuantizeGgufResult result = await store.QuantizeGgufAsync(
            SourceName, Options("Q4_K_M"), null, TestContext.Current.CancellationToken);

        //Assert
        result.Name.Should().Be("local/fixtures/quantizable:gguf-q4_k_m");
        result.SourceName.Should().Be("local/fixtures/quantizable:latest");
        result.Type.Should().Be("q4_k_m");
        (await store.ExistsAsync(result.Name, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task QuantizeGgufAsync_with_an_output_name_stores_it_under_that_name()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        QuantizeGgufOptions options = Options("q8_0");
        options.OutputName = "local/fixtures/quantizable:small";

        //Act
        QuantizeGgufResult result = await store.QuantizeGgufAsync(
            SourceName, options, null, TestContext.Current.CancellationToken);

        //Assert
        result.Name.Should().Be("local/fixtures/quantizable:small");
        (await store.ExistsAsync(result.Name, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task QuantizeGgufAsync_twice_is_refused_until_overwrite_is_set()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        await store.QuantizeGgufAsync(SourceName, Options("q8_0"), null,
            TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = () => store.QuantizeGgufAsync(
            SourceName, Options("q8_0"), null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>()).Which.Message.Should().Contain("Overwrite");
        QuantizeGgufOptions overwriting = Options("q8_0");
        overwriting.Overwrite = true;
        await store.QuantizeGgufAsync(SourceName, overwriting, null,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task QuantizeGgufAsync_records_where_the_model_came_from_and_what_made_it()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);

        //Act
        QuantizeGgufResult result = await store.QuantizeGgufAsync(
            SourceName, Options("q4_k_m"), null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.Format.Should().Be("gguf");
        info.DerivedFrom.Should().Be("local/fixtures/quantizable:latest");
        info.Tool.Should().Be("CodeBrix.Ollama.ModelRunner");
        info.ToolVersion.Should().Be("1.2.3");
        info.Settings["type"].Should().Be("q4_k_m");
        result.Tool.Should().Be("CodeBrix.Ollama.ModelRunner");
        result.ToolVersion.Should().Be("1.2.3");
    }

    [Fact]
    public async Task QuantizeGgufAsync_stores_the_bytes_the_quantizer_wrote()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        byte[] written = new FakeModelBuilder().BuildModelGguf();
        written[written.Length - 1] ^= 0xFF;
        QuantizeGgufOptions options = Options("q8_0");
        options.Quantizer = (input, output, cancellationToken)
            => File.WriteAllBytesAsync(output, written, cancellationToken);

        //Act
        QuantizeGgufResult result = await store.QuantizeGgufAsync(
            SourceName, options, null, TestContext.Current.CancellationToken);

        //Assert
        ResolvedModel resolved = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
        byte[] stored = await File.ReadAllBytesAsync(
            resolved.ModelPath, TestContext.Current.CancellationToken);
        stored.Should().Equal(written);
        result.OutputBytes.Should().Be(written.Length);
    }

    [Fact]
    public async Task QuantizeGgufAsync_hands_the_quantizer_the_stored_blob_and_a_working_file()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        ResolvedModel source = await store.ResolveAsync(SourceName, TestContext.Current.CancellationToken);
        string seenInput = null;
        string seenOutput = null;
        QuantizeGgufOptions options = Options("q8_0");
        options.Quantizer = (input, output, cancellationToken) =>
        {
            seenInput = input;
            seenOutput = output;
            File.Copy(input, output);
            return Task.CompletedTask;
        };

        //Act
        QuantizeGgufResult result = await store.QuantizeGgufAsync(
            SourceName, options, null, TestContext.Current.CancellationToken);

        //Assert
        seenInput.Should().Be(source.ModelPath);
        Path.GetFileName(seenOutput).Should().Be("model.gguf");
        Path.GetDirectoryName(seenOutput).Should().StartWith(
            Path.Combine(Path.GetTempPath(), "codebrix-ollama-quantize-"));
        result.SourceBytes.Should().Be(new FileInfo(source.ModelPath).Length);
        File.Exists(source.ModelPath).Should().BeTrue();
    }

    [Fact]
    public async Task QuantizeGgufAsync_carries_the_sources_template_parameters_and_licence_over()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);

        //Act
        QuantizeGgufResult result = await store.QuantizeGgufAsync(
            SourceName, Options("q8_0"), null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.Template.Should().Be("{{ .Prompt }}");
        info.System.Should().Be("You are a quantizable model.");
        info.Parameters.Temperature.Should().Be(0.25f);
        info.Parameters.Stop.Should().Equal(new List<string> { "<|end|>" });
        info.Licenses.Should().Equal(new List<string> { "Apache-2.0" });
        info.Messages.Should().HaveCount(1);
        info.Messages[0].Content.Should().Be("hello");
    }

    [Fact]
    public async Task QuantizeGgufAsync_reports_its_progress_in_order()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        var progress = new RecordingProgress();

        //Act
        await store.QuantizeGgufAsync(SourceName, Options("q8_0"), progress,
            TestContext.Current.CancellationToken);

        //Assert
        progress.Statuses.Should().Equal(new List<string> { "quantizing", "creating model", "success" });
    }

    [Fact]
    public async Task QuantizeGgufAsync_removes_its_working_folder_after_a_success()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        int before = WorkingFolders();

        //Act
        await store.QuantizeGgufAsync(SourceName, Options("q8_0"), null,
            TestContext.Current.CancellationToken);

        //Assert
        WorkingFolders().Should().Be(before);
    }

    [Fact]
    public async Task QuantizeGgufAsync_removes_its_working_folder_after_a_failure()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        int before = WorkingFolders();
        QuantizeGgufOptions options = Options("q8_0");
        options.Quantizer = (input, output, cancellationToken)
            => throw new InvalidOperationException("the quantizer gave up");
        Func<Task> act = () => store.QuantizeGgufAsync(SourceName, options, null,
            TestContext.Current.CancellationToken);

        //Act
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Be("the quantizer gave up");

        //Assert
        WorkingFolders().Should().Be(before);
        (await store.ExistsAsync("local/fixtures/quantizable:gguf-q8_0",
            TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task QuantizeGgufAsync_with_a_quantizer_that_writes_nothing_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        QuantizeGgufOptions options = Options("q8_0");
        options.Quantizer = (input, output, cancellationToken) => Task.CompletedTask;
        Func<Task> act = () => store.QuantizeGgufAsync(SourceName, options, null,
            TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("without writing");
    }

    [Fact]
    public async Task QuantizeGgufAsync_without_options_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        Func<Task> act = () => store.QuantizeGgufAsync(SourceName, null, null,
            TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("options");
    }

    [Fact]
    public async Task QuantizeGgufAsync_without_a_quantizer_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        Func<Task> act = () => store.QuantizeGgufAsync(
            SourceName, new QuantizeGgufOptions { Type = "q8_0" }, null,
            TestContext.Current.CancellationToken);

        //Act
        ArgumentException thrown = (await act.Should().ThrowAsync<ArgumentException>()).Which;

        //Assert
        thrown.ParamName.Should().Be("options");
        thrown.Message.Should().Contain("Quantizer");
    }

    /// <summary>A type that cannot be part of a model name is refused, and quotes what was given.</summary>
    /// <param name="type">The type to try.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("q4/k/m")]
    [InlineData("-q4")]
    [InlineData(".q4")]
    [InlineData("q4 k m")]
    public async Task QuantizeGgufAsync_with_a_type_that_is_not_a_tag_is_refused(string type)
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        Func<Task> act = () => store.QuantizeGgufAsync(SourceName, Options(type), null,
            TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("options");
    }

    [Fact]
    public async Task QuantizeGgufAsync_of_a_bundle_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        string folder = Path.Combine(directory.DirectoryPath, "bundle");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "config.json"),
            "{\"architectures\":[\"LlamaForCausalLM\"]}", TestContext.Current.CancellationToken);
        await store.ImportBundleAsync(BundleName, folder, null, TestContext.Current.CancellationToken);
        Func<Task> act = () => store.QuantizeGgufAsync(BundleName, Options("q8_0"), null,
            TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("ConvertToGgufAsync");
    }

    [Fact]
    public async Task QuantizeGgufAsync_of_a_model_whose_weights_are_split_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        string folder = Path.Combine(directory.DirectoryPath, "shards");
        Directory.CreateDirectory(folder);
        byte[] first = new FakeModelBuilder().BuildModelGguf();
        byte[] second = new FakeModelBuilder().BuildModelGguf();
        second[second.Length - 1] ^= 0xFF;
        await File.WriteAllBytesAsync(Path.Combine(folder, "a.gguf"), first,
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(folder, "b.gguf"), second,
            TestContext.Current.CancellationToken);
        await store.CreateAsync(
            ShardedName,
            Modelfile.Parse("FROM a.gguf\nFROM b.gguf\n"),
            new CreateOptions { BaseDirectory = folder },
            TestContext.Current.CancellationToken);
        Func<Task> act = () => store.QuantizeGgufAsync(ShardedName, Options("q8_0"), null,
            TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("one file");
    }

    [Fact]
    public async Task QuantizeGgufAsync_of_a_model_that_is_not_there_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        Func<Task> act = () => store.QuantizeGgufAsync("local/fixtures/nothing:latest", Options("q8_0"),
            null, TestContext.Current.CancellationToken);

        //Act and assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }

    [Fact]
    public async Task QuantizeGgufAsync_with_a_cancelled_token_is_cancelled()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = await CreateSourceAsync(directory);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        Func<Task> act = () => store.QuantizeGgufAsync(SourceName, Options("q8_0"), null, source.Token);

        //Act and assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// The options one quantization is asked for: a quantizer that copies the file it is given, so that the
    /// store's own work is what every test is looking at.
    /// </summary>
    /// <param name="type">The type to name.</param>
    /// <returns>The options.</returns>
    private static QuantizeGgufOptions Options(string type)
        => new QuantizeGgufOptions
        {
            Type = type,
            Tool = "CodeBrix.Ollama.ModelRunner",
            ToolVersion = "1.2.3",
            Quantizer = (input, output, cancellationToken) =>
            {
                File.Copy(input, output);
                return Task.CompletedTask;
            }
        };

    /// <summary>
    /// Opens a store holding one GGUF model that carries everything a Modelfile can carry, so that what a
    /// quantization keeps and what it drops are both visible.
    /// </summary>
    /// <param name="directory">The temporary store directory.</param>
    /// <returns>The store. The caller disposes it.</returns>
    private static async Task<ModelStore> CreateSourceAsync(TempStoreDirectory directory)
    {
        var store = new ModelStore(new ModelStoreOptions
        {
            StoreDirectory = directory.DirectoryPath,
            DefaultRegistryHost = "registry.test"
        });

        string folder = Path.Combine(directory.DirectoryPath, "source");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(
            Path.Combine(folder, "model.gguf"), new FakeModelBuilder().BuildModelGguf(), CancellationToken.None);

        await store.CreateAsync(
            SourceName,
            Modelfile.Parse(
                "FROM model.gguf\n"
                + "TEMPLATE {{ .Prompt }}\n"
                + "SYSTEM You are a quantizable model.\n"
                + "PARAMETER temperature 0.25\n"
                + "PARAMETER stop <|end|>\n"
                + "LICENSE Apache-2.0\n"
                + "MESSAGE user hello\n"),
            new CreateOptions { BaseDirectory = folder },
            CancellationToken.None);

        return store;
    }

    /// <summary>
    /// How many working folders of a quantization are under the temporary directory right now, which is how a
    /// test says the last call cleaned up after itself without guessing at the folder's name.
    /// </summary>
    /// <returns>The count.</returns>
    private static int WorkingFolders()
        => Directory.GetDirectories(Path.GetTempPath(), "codebrix-ollama-quantize-*").Length;
}

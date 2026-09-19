using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers converting a stored checkpoint to GGUF: the name the result takes, the provenance it records, the
/// progress it reports, what it refuses, that the working folder never outlives the call, and that the file it
/// stores is the one the inference engine's own converter would have written.
/// </summary>
/// <remarks>
/// The subjects are the checked-in conversion fixtures, imported into a temporary store as a publisher's folder
/// would be. Nothing here needs a network, an interpreter or a model file from anybody else.
/// </remarks>
public sealed class ModelStoreConvertTests
{
    private const string FixtureName = "local/fixtures/tinyllama-123k:latest";
    private const string BareFixtureName = "local/fixtures/tinyllamabare-123k:latest";
    private const string SentencePieceFixtureName = "local/fixtures/tinyllamasp-132k:latest";
    private const string GgufName = "local/fixtures/already-gguf:latest";
    private const string OnnxName = "local/fixtures/graphs:latest";
    private const string UnknownName = "local/fixtures/unknown-family:latest";
    private const string WeightlessName = "local/fixtures/no-weights:latest";

    [Fact]
    public async Task ConvertToGgufAsync_stores_the_result_under_the_gguf_tag()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        result.Name.Should().Be("local/fixtures/tinyllama-123k:gguf");
        result.Architecture.Should().Be(CheckpointArchitecture.Llama);
        result.TypeWritten.Should().Be(GgufOutputType.BF16);
        result.TensorCount.Should().Be(21);
        result.Tool.Should().Be("CodeBrix.Ollama.ModelManager");
        result.ToolVersion.Should().NotBeNullOrEmpty();
        (await store.ExistsAsync(result.Name, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Theory]
    [InlineData(GgufOutputType.F16, "local/fixtures/tinyllama-123k:gguf-f16")]
    [InlineData(GgufOutputType.F32, "local/fixtures/tinyllama-123k:gguf-f32")]
    [InlineData(GgufOutputType.BF16, "local/fixtures/tinyllama-123k:gguf-bf16")]
    public async Task ConvertToGgufAsync_names_a_forced_type_in_the_tag(
        GgufOutputType outputType, string expected)
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");
        var options = new ConvertOptions { OutputType = outputType };

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, options, null, TestContext.Current.CancellationToken);

        //Assert
        result.Name.Should().Be(expected);
        result.TypeWritten.Should().Be(outputType);
    }

    [Fact]
    public async Task ConvertToGgufAsync_with_an_output_name_stores_it_under_that_name()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");
        var options = new ConvertOptions { OutputName = "local/fixtures/tinyllama-123k:weights" };

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, options, null, TestContext.Current.CancellationToken);

        //Assert
        result.Name.Should().Be("local/fixtures/tinyllama-123k:weights");
        (await store.ExistsAsync(result.Name, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertToGgufAsync_twice_is_refused_until_overwrite_is_set()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");
        await store.ConvertToGgufAsync(FixtureName, null, null, TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = () => store.ConvertToGgufAsync(
            FixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("Overwrite");
        await store.ConvertToGgufAsync(
            FixtureName, new ConvertOptions { Overwrite = true }, null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConvertToGgufAsync_with_overwrite_replaces_what_was_there()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");
        await store.ConvertToGgufAsync(
            FixtureName,
            new ConvertOptions { OutputName = "local/fixtures/tinyllama-123k:one" },
            null,
            TestContext.Current.CancellationToken);
        var options = new ConvertOptions
        {
            OutputName = "local/fixtures/tinyllama-123k:one",
            OutputType = GgufOutputType.F32,
            Overwrite = true
        };

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, options, null, TestContext.Current.CancellationToken);

        //Assert
        result.TypeWritten.Should().Be(GgufOutputType.F32);
        ResolvedModel resolved = await store.ResolveAsync(
            "local/fixtures/tinyllama-123k:one", TestContext.Current.CancellationToken);
        new FileInfo(resolved.ModelPath).Length.Should().Be(result.OutputBytes);
    }

    [Fact]
    public async Task ConvertToGgufAsync_stores_the_bytes_the_engines_own_converter_writes()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");
        byte[] oracle = await File.ReadAllBytesAsync(
            ConvertFixtureFiles.OraclePath("tinyllama-123k", "auto"), TestContext.Current.CancellationToken);

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        ResolvedModel resolved = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
        byte[] stored = await File.ReadAllBytesAsync(resolved.ModelPath, TestContext.Current.CancellationToken);
        stored.Length.Should().Be(oracle.Length);
        stored.SequenceEqual(oracle).Should().BeTrue();
        result.OutputBytes.Should().Be(oracle.Length);
    }

    [Fact]
    public async Task ConvertToGgufAsync_stores_a_sentencepiece_checkpoint_as_the_engine_writes_it()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string variant = ConvertFixtureFiles.SentencePieceVariant;
        await ImportFixtureAsync(store, SentencePieceFixtureName, variant);
        byte[] oracle = await File.ReadAllBytesAsync(
            ConvertFixtureFiles.OraclePath(variant, "auto"), TestContext.Current.CancellationToken);

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            SentencePieceFixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        ResolvedModel resolved = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
        byte[] stored = await File.ReadAllBytesAsync(resolved.ModelPath, TestContext.Current.CancellationToken);
        stored.Length.Should().Be(oracle.Length);
        stored.SequenceEqual(oracle).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConvertToGgufAsync_materializes_every_shard_of_a_split_checkpoint(bool safetensors)
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        (string safetensorsVariant, string pickleVariant) = ConvertFixtureFiles.ShardedPair;
        string variant = safetensors ? safetensorsVariant : pickleVariant;
        string name = "local/fixtures/" + variant + ":latest";
        await ImportFixtureAsync(store, name, variant);
        byte[] oracle = await File.ReadAllBytesAsync(
            ConvertFixtureFiles.OraclePath(variant, "auto"), TestContext.Current.CancellationToken);

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            name, null, null, TestContext.Current.CancellationToken);

        //Assert
        result.TensorCount.Should().Be(21);
        ResolvedModel resolved = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
        byte[] stored = await File.ReadAllBytesAsync(resolved.ModelPath, TestContext.Current.CancellationToken);
        stored.SequenceEqual(oracle).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertToGgufAsync_refuses_supplied_special_tokens_for_a_sentencepiece_checkpoint()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, SentencePieceFixtureName, ConvertFixtureFiles.SentencePieceVariant);
        var options = new ConvertOptions { AddedSpecialTokens = new[] { "<pad>" } };

        //Act
        Func<Task> act = () => store.ConvertToGgufAsync(
            SentencePieceFixtureName, options, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("options");
        WorkingFolders().Should().Be(0);
    }

    [Fact]
    public async Task ConvertToGgufAsync_passes_the_supplied_special_tokens_to_the_conversion()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, BareFixtureName, ConvertFixtureFiles.UndeclaredSpecialsVariant);
        byte[] oracle = await File.ReadAllBytesAsync(
            ConvertFixtureFiles.OraclePath(ConvertFixtureFiles.UndeclaredSpecialsVariant, "auto"),
            TestContext.Current.CancellationToken);
        var options = new ConvertOptions { AddedSpecialTokens = ConvertFixtureFiles.UndeclaredSpecialTokens };

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            BareFixtureName, options, null, TestContext.Current.CancellationToken);

        //Assert
        ResolvedModel resolved = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
        byte[] stored = await File.ReadAllBytesAsync(resolved.ModelPath, TestContext.Current.CancellationToken);
        stored.Length.Should().Be(oracle.Length);
        stored.Where((value, index) => value != oracle[index]).Should().HaveCount(4);
    }

    [Fact]
    public async Task ConvertToGgufAsync_with_a_supplied_token_that_is_not_in_the_vocabulary_names_it()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");
        var options = new ConvertOptions { AddedSpecialTokens = new[] { "<not-in-this-vocabulary>" } };

        //Act
        Func<Task> act = () => store.ConvertToGgufAsync(
            FixtureName, options, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("<not-in-this-vocabulary>");
    }

    [Fact]
    public async Task ConvertToGgufAsync_records_the_provenance_of_what_it_made()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");
        var options = new ConvertOptions { OutputType = GgufOutputType.F16 };

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, options, null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.DerivedFrom.Should().Be(FixtureName);
        info.Tool.Should().Be("CodeBrix.Ollama.ModelManager");
        info.ToolVersion.Should().Be(result.ToolVersion);
        info.Format.Should().Be("gguf");
        info.Settings["outputType"].Should().Be("F16");
        info.Settings["requestedOutputType"].Should().Be("F16");
        info.Settings["architecture"].Should().Be("Llama");
        info.Settings["requestedArchitecture"].Should().Be("Auto");
        info.Settings["modelId"].Should().Be("tinyllama-123k");
        info.Settings["addedSpecialTokens"].Should().BeEmpty();
    }

    [Fact]
    public async Task ConvertToGgufAsync_records_the_supplied_special_tokens_in_its_settings()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, BareFixtureName, ConvertFixtureFiles.UndeclaredSpecialsVariant);
        var options = new ConvertOptions { AddedSpecialTokens = ConvertFixtureFiles.UndeclaredSpecialTokens };

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            BareFixtureName, options, null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.Settings["addedSpecialTokens"].Should().Be("<pad>,<unk>,<bos>,<eos>");
    }

    [Fact]
    public async Task ConvertToGgufAsync_stores_a_model_the_gguf_reader_can_read()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.Metadata.Architecture.Should().Be("llama");
        info.Metadata.Kind.Should().Be("model");
        info.Capabilities.Should().Contain(ModelCapability.Completion);
    }

    [Fact]
    public async Task ConvertToGgufAsync_records_the_source_model_on_the_layer_it_made()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        //The file the Modelfile named was a temporary one that is gone by now, so what the layer records
        //having come from is the MODEL it was converted from, which is a name that still means something.
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        ModelLayer model = info.Manifest.Layers.Single(layer => layer.MediaType == MediaTypes.Model);
        model.From.Should().Be(FixtureName);
    }

    [Fact]
    public async Task ConvertToGgufAsync_carries_the_licence_the_source_states()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.License.LicenseId.Should().Be("mit");
        info.License.LicenseSource.Should().Be("https://example.test/fixtures");
    }

    [Fact]
    public async Task ConvertToGgufAsync_carries_a_licence_text_the_source_ships()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        string root = await CopyFixtureAsync(directory, "tinyllama-123k");
        await File.WriteAllTextAsync(
            Path.Combine(root, "LICENSE"), "Everything here is ours, MIT licensed.",
            TestContext.Current.CancellationToken);
        await store.ImportBundleAsync(FixtureName, root, null, TestContext.Current.CancellationToken);

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.Licenses.Should().ContainSingle();
        info.Licenses[0].Should().Contain("MIT licensed");
    }

    [Fact]
    public async Task ConvertToGgufAsync_writes_a_stop_parameter_the_tokenizer_configuration_names()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            FixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        ResolvedModel resolved = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
        resolved.Parameters.Stop.Should().Equal("<eos>");
        resolved.Template.Should().BeNull();
    }

    [Fact]
    public async Task ConvertToGgufAsync_writes_no_stop_parameter_when_the_files_name_none()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, BareFixtureName, ConvertFixtureFiles.UndeclaredSpecialsVariant);

        //Act
        ConvertResult result = await store.ConvertToGgufAsync(
            BareFixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        ResolvedModel resolved = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
        resolved.Parameters.Should().BeNull();
    }

    [Fact]
    public async Task ConvertToGgufAsync_reports_the_statuses_a_caller_can_show()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");
        var progress = new RecordingProgress();

        //Act
        await store.ConvertToGgufAsync(FixtureName, null, progress, TestContext.Current.CancellationToken);

        //Assert
        progress.Statuses.Should().Equal(
            "materializing source", "reading checkpoint", "writing gguf", "creating model", "success");
    }

    [Fact]
    public async Task ConvertToGgufAsync_of_a_model_that_already_holds_a_gguf_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFolderAsync(store, directory, GgufName, "gguf-source", new Dictionary<string, string>
        {
            ["config.json"] = "{\"architectures\":[\"LlamaForCausalLM\"]}",
            ["model.gguf"] = "not really a gguf, and nothing reads it"
        });

        //Act
        Func<Task> act = () => store.ConvertToGgufAsync(
            GgufName, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("model.gguf");
    }

    [Fact]
    public async Task ConvertToGgufAsync_of_an_onnx_bundle_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFolderAsync(store, directory, OnnxName, "onnx-source", new Dictionary<string, string>
        {
            ["config.json"] = "{\"architectures\":[\"LlamaForCausalLM\"]}",
            ["model.onnx"] = "the exported graph"
        });

        //Act
        Func<Task> act = () => store.ConvertToGgufAsync(
            OnnxName, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("model.onnx");
    }

    [Fact]
    public async Task ConvertToGgufAsync_of_an_architecture_this_version_cannot_read_names_it()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFolderAsync(store, directory, UnknownName, "unknown-source", new Dictionary<string, string>
        {
            ["config.json"] = "{\"architectures\":[\"GPT2LMHeadModel\"],\"model_type\":\"gpt2\"}",
            ["pytorch_model.bin"] = "the weights, near enough for a refusal"
        });

        //Act
        Func<Task> act = () => store.ConvertToGgufAsync(
            UnknownName, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("GPT2LMHeadModel");
    }

    [Fact]
    public async Task ConvertToGgufAsync_of_a_bundle_with_no_configuration_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFolderAsync(store, directory, UnknownName, "no-config", new Dictionary<string, string>
        {
            ["pytorch_model.bin"] = "the weights and nothing else"
        });

        //Act
        Func<Task> act = () => store.ConvertToGgufAsync(
            UnknownName, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CheckpointFormatException>())
            .Which.Message.Should().Contain("config.json");
    }

    [Fact]
    public async Task ConvertToGgufAsync_of_a_model_that_is_not_a_bundle_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        Func<Task> act = () => store.ConvertToGgufAsync(
            builder.Reference, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("no publisher file tree");
    }

    [Fact]
    public async Task ConvertToGgufAsync_of_a_model_that_is_not_there_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));

        //Act
        Func<Task> act = () => store.ConvertToGgufAsync(
            "local/nobody/nothing:latest", null, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }

    [Fact]
    public async Task ConvertToGgufAsync_with_a_cancelled_token_does_not_convert()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        //Act
        Func<Task> act = () => store.ConvertToGgufAsync(FixtureName, null, null, cancellation.Token);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.ExistsAsync("local/fixtures/tinyllama-123k:gguf", TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ConvertToGgufAsync_removes_its_working_folder_when_it_succeeds()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportFixtureAsync(store, FixtureName, "tinyllama-123k");
        int before = WorkingFolders();

        //Act
        await store.ConvertToGgufAsync(FixtureName, null, null, TestContext.Current.CancellationToken);

        //Assert
        WorkingFolders().Should().Be(before);
    }

    [Fact]
    public async Task ConvertToGgufAsync_removes_its_working_folder_when_the_conversion_fails()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        //A whole checkpoint with its weights removed: it passes every check that is made before anything is
        //laid out, so the conversion really starts, really fails, and really has a folder to clean up.
        string root = await CopyFixtureAsync(directory, "tinyllama-123k");
        File.Delete(Path.Combine(root, "model.safetensors"));
        await store.ImportBundleAsync(WeightlessName, root, null, TestContext.Current.CancellationToken);
        int before = WorkingFolders();
        Func<Task> act = () => store.ConvertToGgufAsync(
            WeightlessName, null, null, TestContext.Current.CancellationToken);

        //Act and assert
        (await act.Should().ThrowAsync<CheckpointFormatException>())
            .Which.Message.Should().Contain("no weights to convert");
        WorkingFolders().Should().Be(before);
    }

    /// <summary>
    /// The options every test here uses: a temporary store directory and nothing else.
    /// </summary>
    /// <param name="directory">The temporary store directory.</param>
    /// <returns>The options.</returns>
    private static ModelStoreOptions CreateOptions(TempStoreDirectory directory)
        => new ModelStoreOptions
        {
            StoreDirectory = directory.DirectoryPath,
            DefaultRegistryHost = "registry.test"
        };

    /// <summary>
    /// How many working folders of a conversion are under the temporary directory right now, which is how a
    /// test says the last call cleaned up after itself without guessing at the folder's name.
    /// </summary>
    /// <returns>The count.</returns>
    private static int WorkingFolders()
        => Directory.GetDirectories(Path.GetTempPath(), "codebrix-ollama-convert-*").Length;

    /// <summary>
    /// Imports one of the checked-in conversion fixtures as a bundle, under a name whose last segment is the
    /// fixture's folder name - which is what the oracle's general metadata was derived from.
    /// </summary>
    /// <param name="store">The store to import into.</param>
    /// <param name="name">The name to import under.</param>
    /// <param name="variant">The fixture variant.</param>
    /// <returns>A task that completes when the bundle is in the store.</returns>
    private static Task ImportFixtureAsync(ModelStore store, string name, string variant)
    {
        var options = new ImportOptions
        {
            License = new LicenseRecord("mit", "https://example.test/fixtures", null)
        };
        return store.ImportBundleAsync(
            name, ConvertFixtureFiles.CheckpointPath(variant), options, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Copies a fixture into a folder of the test's own, so that a test can add a file to it.
    /// </summary>
    /// <param name="directory">The temporary store directory the copy is made inside.</param>
    /// <param name="variant">The fixture variant.</param>
    /// <returns>The folder the copy was written to.</returns>
    private static async Task<string> CopyFixtureAsync(TempStoreDirectory directory, string variant)
    {
        string root = Path.Combine(directory.DirectoryPath, variant);
        Directory.CreateDirectory(root);
        foreach (string path in Directory.GetFiles(ConvertFixtureFiles.CheckpointPath(variant)))
        {
            using FileStream source = File.OpenRead(path);
            using FileStream target = File.Create(Path.Combine(root, Path.GetFileName(path)));
            await source.CopyToAsync(target, TestContext.Current.CancellationToken);
        }

        return root;
    }

    /// <summary>
    /// Writes a folder of small text files and imports it, which is how the refusal tests build a source that
    /// is the wrong shape without carrying a fixture for each one.
    /// </summary>
    /// <param name="store">The store to import into.</param>
    /// <param name="directory">The temporary store directory the folder is written inside.</param>
    /// <param name="name">The name to import under.</param>
    /// <param name="folder">The folder's name.</param>
    /// <param name="files">The files, by relative path.</param>
    /// <returns>A task that completes when the bundle is in the store.</returns>
    private static async Task ImportFolderAsync(ModelStore store, TempStoreDirectory directory, string name,
        string folder, IReadOnlyDictionary<string, string> files)
    {
        string root = Path.Combine(directory.DirectoryPath, folder);
        Directory.CreateDirectory(root);
        foreach (KeyValuePair<string, string> file in files)
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, file.Key), file.Value, TestContext.Current.CancellationToken);
        }

        await store.ImportBundleAsync(name, root, null, TestContext.Current.CancellationToken);
    }
}

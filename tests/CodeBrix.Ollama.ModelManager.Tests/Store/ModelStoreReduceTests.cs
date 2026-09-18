using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers reducing a model as far as it can be covered with no Python on the machine: the name the
/// derived bundle takes, which graphs are reduced, what is carried through beside them, what the bundle
/// records about the run, what replacing one costs, and the message a machine that cannot run the tools
/// gives.
/// </summary>
/// <remarks>
/// THE ARITHMETIC IS NOT WHAT THESE TESTS ARE ABOUT. The quantizers are ONNX Runtime's, they run in a
/// CPython, and the tests that really run them are in the Python test project behind two gates. Here the
/// engine is replaced by <see cref="OnnxReduce.RunOverrideForTesting"/> - a delegate that writes a file
/// of the size a real reduction would have written - so that everything AROUND the arithmetic is tested
/// on every machine, in milliseconds, with no interpreter anywhere near the process. Each test sets the
/// replacement back in a finally; the suite runs serially, which is what makes one static seam safe.
/// </remarks>
public sealed class ModelStoreReduceTests
{
    private const string OnnxBundleName = "local/skytnt/midi-model:onnx";
    private const string PlainBundleName = "local/skytnt/midi-model:latest";
    private const string CheckpointName = "local/m-a-p/mupt:latest";

    /// <summary>The size of the base graph the test bundle ships.</summary>
    private const long BaseGraphBytes = 4096L;

    /// <summary>The size of the token graph the test bundle ships.</summary>
    private const long TokenGraphBytes = 1024L;

    /// <summary>The size the replacement engine writes when a test measures what it wrote.</summary>
    private const long ReducedSize = 512L;

    [Fact]
    public async Task ReduceOnnxAsync_reduces_every_graph_and_names_the_bundle_after_the_mode()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);

        //Act
        ReduceResult result = await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(OnnxBundleName, null, null, TestContext.Current.CancellationToken));

        //Assert
        result.Name.Should().Be("local/skytnt/midi-model:onnx-int8");
        result.Mode.Should().Be(ReduceMode.DynamicInt8);
        result.EngineUsed.Should().Be(ReduceEngine.Python);
        result.Tool.Should().Be("onnxruntime");
        result.ToolVersion.Should().Be("1.30.0");
        result.Files.Should().Equal(
            "README.md", "config.json", "onnx/model_base.onnx", "onnx/model_token.onnx");
    }

    [Theory]
    [InlineData(ReduceMode.DynamicInt8, "local/skytnt/midi-model:onnx-int8")]
    [InlineData(ReduceMode.WeightOnlyInt8, "local/skytnt/midi-model:onnx-int8-weights")]
    [InlineData(ReduceMode.WeightOnlyInt4, "local/skytnt/midi-model:onnx-int4-weights")]
    [InlineData(ReduceMode.PreprocessOnly, "local/skytnt/midi-model:onnx-preprocessed")]
    public async Task ReduceOnnxAsync_adds_the_modes_tag_to_the_one_the_source_carries(
        ReduceMode mode, string expected)
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);

        //Act
        ReduceResult result = await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(
                OnnxBundleName, new ReduceOptions { Mode = mode }, null,
                TestContext.Current.CancellationToken));

        //Assert
        result.Name.Should().Be(expected);
        (await store.ExistsAsync(expected, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task ReduceOnnxAsync_of_a_source_with_no_tag_of_its_own_uses_the_modes_tag_alone()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, PlainBundleName);

        //Act
        ReduceResult result = await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(
                PlainBundleName, new ReduceOptions { Mode = ReduceMode.WeightOnlyInt4 }, null,
                TestContext.Current.CancellationToken));

        //Assert
        result.Name.Should().Be("local/skytnt/midi-model:int4-weights");
    }

    [Fact]
    public async Task ReduceOnnxAsync_carries_the_files_it_did_not_write_through_unchanged()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);
        ResolvedModel source = await store.ResolveAsync(
            OnnxBundleName, TestContext.Current.CancellationToken);

        //Act
        ReduceResult result = await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(OnnxBundleName, null, null, TestContext.Current.CancellationToken));

        //Assert
        ResolvedModel derived = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
        foreach (string carried in new[] { "README.md", "config.json" })
        {
            derived.Files.Single(file => file.Name == carried).Digest
                .Should().Be(source.Files.Single(file => file.Name == carried).Digest);
        }
        derived.Files.Single(file => file.Name == "onnx/model_base.onnx").Digest
            .Should().NotBe(source.Files.Single(file => file.Name == "onnx/model_base.onnx").Digest);
        derived.Files.Select(file => file.Name).Should().NotContain("model.safetensors");
    }

    [Fact]
    public async Task ReduceOnnxAsync_with_one_file_named_leaves_the_other_graph_as_it_was()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);
        ResolvedModel source = await store.ResolveAsync(
            OnnxBundleName, TestContext.Current.CancellationToken);
        var options = new ReduceOptions { Files = new[] { "onnx/model_token.onnx" } };

        //Act
        ReduceResult result = await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(
                OnnxBundleName, options, null, TestContext.Current.CancellationToken));

        //Assert
        ResolvedModel derived = await store.ResolveAsync(result.Name, TestContext.Current.CancellationToken);
        derived.Files.Single(file => file.Name == "onnx/model_base.onnx").Digest
            .Should().Be(source.Files.Single(file => file.Name == "onnx/model_base.onnx").Digest);
        derived.Files.Single(file => file.Name == "onnx/model_token.onnx").Digest
            .Should().NotBe(source.Files.Single(file => file.Name == "onnx/model_token.onnx").Digest);

        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.Settings["files"].Should().Be("onnx/model_token.onnx");
    }

    [Fact]
    public async Task ReduceOnnxAsync_measures_only_the_graphs_it_reduced()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);

        //Act
        ReduceResult result = await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(OnnxBundleName, null, null, TestContext.Current.CancellationToken),
            ReducedSize);

        //Assert
        result.SourceBytes.Should().Be(BaseGraphBytes + TokenGraphBytes);
        result.ReducedBytes.Should().Be(2 * ReducedSize);
        result.ToString().Should().Contain("smaller");
    }

    [Fact]
    public async Task ReduceOnnxAsync_records_the_provenance_a_reduction_has()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);
        var options = new ReduceOptions
        {
            Mode = ReduceMode.WeightOnlyInt4,
            BlockSize = 32,
            IsSymmetric = true,
            AccuracyLevel = 4
        };

        //Act
        ReduceResult result = await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(
                OnnxBundleName, options, null, TestContext.Current.CancellationToken));

        //Assert
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.DerivedFrom.Should().Be(OnnxBundleName);
        info.Tool.Should().Be("onnxruntime");
        info.ToolVersion.Should().Be("1.30.0");
        info.Format.Should().Be("onnx");
        info.License.LicenseId.Should().Be("apache-2.0");
        info.Settings["mode"].Should().Be("WeightOnlyInt4");
        info.Settings["engine"].Should().Be("Managed");
        info.Settings["requestedEngine"].Should().Be("Auto");
        info.Settings["blockSize"].Should().Be("32");
        info.Settings["isSymmetric"].Should().Be("true");
        info.Settings["accuracyLevel"].Should().Be("4");
        info.Settings["preprocess"].Should().Be("false");
    }

    [Fact]
    public async Task ReduceOnnxAsync_reports_the_statuses_a_caller_can_show()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);
        var progress = new RecordingProgress();

        //Act
        await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(
                OnnxBundleName, null, progress, TestContext.Current.CancellationToken));

        //Assert
        progress.Statuses.Should().Equal(
            "materializing source",
            "reducing onnx/model_base.onnx",
            "reducing onnx/model_token.onnx",
            "collecting",
            "writing manifest",
            "success");
    }

    [Fact]
    public async Task ReduceOnnxAsync_with_an_output_name_stores_it_under_that_name()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);
        var options = new ReduceOptions { OutputName = "local/skytnt/midi-model:small" };

        //Act
        ReduceResult result = await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(
                OnnxBundleName, options, null, TestContext.Current.CancellationToken));

        //Assert
        result.Name.Should().Be("local/skytnt/midi-model:small");
        (await store.ExistsAsync(result.Name, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task ReduceOnnxAsync_twice_is_refused_until_overwrite_is_set()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);
        await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(OnnxBundleName, null, null, TestContext.Current.CancellationToken));

        //Act
        Func<Task> act = () => WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(OnnxBundleName, null, null, TestContext.Current.CancellationToken));

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("Overwrite");
        await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(
                OnnxBundleName, new ReduceOptions { Overwrite = true }, null,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReduceOnnxAsync_can_reduce_a_bundle_this_library_reduced_before()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);
        ReduceResult prepared = await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(
                OnnxBundleName, new ReduceOptions { Mode = ReduceMode.PreprocessOnly }, null,
                TestContext.Current.CancellationToken));

        //Act
        ReduceResult result = await WithStubbedEngineAsync(
            () => store.ReduceOnnxAsync(
                prepared.Name, new ReduceOptions { Mode = ReduceMode.WeightOnlyInt4 }, null,
                TestContext.Current.CancellationToken));

        //Assert
        result.Name.Should().Be("local/skytnt/midi-model:onnx-preprocessed-int4-weights");
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.DerivedFrom.Should().Be(prepared.Name);
    }

    [Fact]
    public async Task ReduceOnnxAsync_with_the_managed_engine_refuses_to_prepare_a_graph()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);
        var options = new ReduceOptions
        {
            Engine = ReduceEngine.Managed,
            Mode = ReduceMode.PreprocessOnly
        };

        //Act
        Func<Task> act = () => store.ReduceOnnxAsync(
            OnnxBundleName, options, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("ReduceEngine.Auto");
    }

    [Fact]
    public async Task ReduceOnnxAsync_of_a_bundle_with_no_graph_says_to_export_one_first()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportCheckpointBundleAsync(store, directory);

        //Act
        Func<Task> act = () => store.ReduceOnnxAsync(
            CheckpointName, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelManagerException>())
            .Which.Message.Should().Contain("ExportToOnnxAsync");
    }

    [Fact]
    public async Task ReduceOnnxAsync_of_a_model_that_is_not_a_bundle_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);

        //Act
        Func<Task> act = () => store.ReduceOnnxAsync(
            builder.Reference, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("no publisher file tree");
    }

    [Fact]
    public async Task ReduceOnnxAsync_of_a_model_that_is_not_there_is_refused()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));

        //Act
        Func<Task> act = () => store.ReduceOnnxAsync(
            "local/nobody/nothing:latest", null, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }

    [Fact]
    public async Task ReduceOnnxAsync_with_no_python_says_which_feature_needed_it()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        ModelStoreOptions options = CreateOptions(directory);
        //A library path that is not there is what makes this test the same on every machine, whether or
        //not the one it runs on has a CPython of its own: nothing is started, and nothing can be.
        options.Python.LibraryPath = Path.Combine(directory.DirectoryPath, "no-such-libpython3.13.so");
        using var store = new ModelStore(options);
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);

        //Act
        Func<Task> act = () => store.ReduceOnnxAsync(
            OnnxBundleName, new ReduceOptions { Engine = ReduceEngine.Python }, null,
            TestContext.Current.CancellationToken);

        //Assert
        PythonNotAvailableException thrown = (await act.Should().ThrowAsync<PythonNotAvailableException>()).Which;
        thrown.Feature.Should().Be("reducing an ONNX model");
        thrown.Message.Should().Contain("reducing an ONNX model");
    }

    [Fact]
    public async Task ReduceOnnxAsync_of_an_unprepared_graph_with_no_python_says_what_to_do_instead()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        ModelStoreOptions options = CreateOptions(directory);
        options.Python.LibraryPath = Path.Combine(directory.DirectoryPath, "no-such-libpython3.13.so");
        using var store = new ModelStore(options);
        await ImportOnnxBundleAsync(store, directory, OnnxBundleName);

        //Act
        Func<Task> act = () => store.ReduceOnnxAsync(
            OnnxBundleName, null, null, TestContext.Current.CancellationToken);

        //Assert
        ModelManagerException thrown = (await act.Should().ThrowAsync<ModelManagerException>()).Which;
        thrown.Message.Should().Contain("ReduceMode.PreprocessOnly");
        thrown.Message.Should().Contain("ReduceMode.WeightOnlyInt4");
    }

    [Fact]
    public async Task ReduceOnnxAsync_with_the_managed_engine_needs_no_python_at_all()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        ModelStoreOptions options = CreateOptions(directory);
        //Nothing here could start an interpreter even if something wanted to.
        options.Python.LibraryPath = Path.Combine(directory.DirectoryPath, "no-such-libpython3.13.so");
        using var store = new ModelStore(options);
        await ImportRealGraphBundleAsync(store, directory, OnnxBundleName);

        //Act
        ReduceResult result = await store.ReduceOnnxAsync(
            OnnxBundleName,
            new ReduceOptions { Mode = ReduceMode.WeightOnlyInt4, BlockSize = 32 },
            null,
            TestContext.Current.CancellationToken);

        //Assert
        result.EngineUsed.Should().Be(ReduceEngine.Managed);
        result.Tool.Should().Be("CodeBrix.Ollama.ModelManager");
        result.ToolVersion.Should().Be(typeof(ModelStore).Assembly.GetName().Version.ToString());
        result.ReducedBytes.Should().BeLessThan(result.SourceBytes);
        result.Files.Should().Contain("onnx/model_base.onnx");
    }

    [Fact]
    public async Task ReduceOnnxAsync_with_the_managed_engine_writes_what_the_oracle_wrote()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportRealGraphBundleAsync(store, directory, OnnxBundleName);
        var target = new TempStoreDirectory();

        //Act
        ReduceResult result = await store.ReduceOnnxAsync(
            OnnxBundleName,
            new ReduceOptions { Mode = ReduceMode.WeightOnlyInt4, BlockSize = 32, Engine = ReduceEngine.Managed },
            null,
            TestContext.Current.CancellationToken);

        //Assert
        using (target)
        {
            await store.MaterializeAsync(
                result.Name, target.DirectoryPath, null, TestContext.Current.CancellationToken);
            byte[] written = await File.ReadAllBytesAsync(
                Path.Combine(target.DirectoryPath, "onnx", "model_base.onnx"),
                TestContext.Current.CancellationToken);
            byte[] oracle = await File.ReadAllBytesAsync(
                OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.nbits_b4_bs32_asym.onnx"),
                TestContext.Current.CancellationToken);
            written.Should().Equal(oracle);
        }
    }

    [Fact]
    public async Task ReduceOnnxAsync_with_the_managed_engine_reads_weights_kept_beside_the_graph()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportRealGraphBundleAsync(
            store, directory, OnnxBundleName, "external_gather_fp32.onnx", "external_gather_fp32.onnx");
        var target = new TempStoreDirectory();

        //Act
        ReduceResult result = await store.ReduceOnnxAsync(
            OnnxBundleName,
            new ReduceOptions { Mode = ReduceMode.WeightOnlyInt4, BlockSize = 32, Engine = ReduceEngine.Managed },
            null,
            TestContext.Current.CancellationToken);

        //Assert
        using (target)
        {
            await store.MaterializeAsync(
                result.Name, target.DirectoryPath, null, TestContext.Current.CancellationToken);
            string graph = Path.Combine(target.DirectoryPath, "onnx", "external_gather_fp32.onnx");
            File.Exists(graph + ".data").Should().BeFalse(
                "a graph small enough to be written in one piece is written in one piece, whatever shape it"
                + " arrived in - which is what the Python engine does with the same graph");
            byte[] written = await File.ReadAllBytesAsync(graph, TestContext.Current.CancellationToken);
            byte[] oracle = await File.ReadAllBytesAsync(
                OnnxFixtureFiles.FullPath("external_gather_fp32.nbits_b4_bs32_asym.onnx"),
                TestContext.Current.CancellationToken);
            written.Should().Equal(oracle);
        }
    }

    [Fact]
    public async Task ReduceOnnxAsync_with_the_managed_engine_records_it_as_the_tool()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportRealGraphBundleAsync(store, directory, OnnxBundleName);

        //Act
        ReduceResult result = await store.ReduceOnnxAsync(
            OnnxBundleName,
            new ReduceOptions { Mode = ReduceMode.WeightOnlyInt8, Engine = ReduceEngine.Managed },
            null,
            TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync(result.Name, TestContext.Current.CancellationToken);
        info.Tool.Should().Be("CodeBrix.Ollama.ModelManager");
        info.ToolVersion.Should().Be(typeof(ModelStore).Assembly.GetName().Version.ToString());
        info.Settings["engine"].Should().Be("Managed");
        info.Settings["requestedEngine"].Should().Be("Managed");
        info.DerivedFrom.Should().Be(OnnxBundleName);
    }

    [Fact]
    public async Task ReduceOnnxAsync_with_the_managed_engine_refuses_a_graph_nobody_prepared()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportRealGraphBundleAsync(store, directory, OnnxBundleName);
        var options = new ReduceOptions { Mode = ReduceMode.DynamicInt8, Engine = ReduceEngine.Managed };

        //Act
        Func<Task> act = () => store.ReduceOnnxAsync(
            OnnxBundleName, options, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("onnx.infer");
    }

    [Fact]
    public async Task ReduceOnnxAsync_of_a_prepared_graph_takes_the_managed_engine_by_itself()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var store = new ModelStore(CreateOptions(directory));
        await ImportRealGraphBundleAsync(
            store, directory, OnnxBundleName, "matmul_k64_n8_fp32.inferred.onnx");

        //Act
        ReduceResult result = await store.ReduceOnnxAsync(
            OnnxBundleName, new ReduceOptions { Mode = ReduceMode.DynamicInt8 }, null,
            TestContext.Current.CancellationToken);

        //Assert
        result.EngineUsed.Should().Be(
            ReduceEngine.Managed, "a graph that records shape inference needs no interpreter");
        result.Tool.Should().Be("CodeBrix.Ollama.ModelManager");
    }

    /// <summary>
    /// Runs one reduction with the engine replaced by a delegate that writes a file where a real engine
    /// would have written one, and puts the real engine back afterwards.
    /// </summary>
    /// <typeparam name="T">What the reduction returns.</typeparam>
    /// <param name="work">The reduction to run.</param>
    /// <param name="writtenBytes">
    /// The size of the file the replacement writes, or 0 for a quarter of what it was given.
    /// </param>
    /// <returns>What the reduction returned.</returns>
    private static async Task<T> WithStubbedEngineAsync<T>(Func<Task<T>> work, long writtenBytes = 0)
    {
        OnnxReduce.RunOverrideForTesting = (mode, options, inputPath, outputPath) =>
        {
            long size = writtenBytes > 0 ? writtenBytes : Math.Max(1L, new FileInfo(inputPath).Length / 4);
            File.WriteAllBytes(outputPath, new byte[size]);
            return Task.FromResult(new OnnxReduceRun(
                "onnxruntime", "1.30.0", new[] { outputPath }, size));
        };

        try
        {
            return await work();
        }
        finally
        {
            OnnxReduce.RunOverrideForTesting = null;
        }
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
    /// Imports a bundle shaped like one an export produced: two graphs in a folder of their own, the
    /// files that go with them, and a checkpoint an export would already have left behind.
    /// </summary>
    /// <param name="store">The store to import into.</param>
    /// <param name="directory">The temporary directory the folder is written inside.</param>
    /// <param name="name">The name to import it under.</param>
    /// <returns>A task that completes when the bundle is in the store.</returns>
    private static async Task ImportOnnxBundleAsync(
        ModelStore store, TempStoreDirectory directory, string name)
    {
        string root = Path.Combine(directory.DirectoryPath, "onnx-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "onnx"));

        await File.WriteAllTextAsync(
            Path.Combine(root, "config.json"), "{\"architectures\":[\"MidiModel\"]}",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "README.md"), "# a model card", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "model.safetensors"), "the checkpoint the graphs came from",
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(root, "onnx", "model_base.onnx"), new byte[BaseGraphBytes],
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(root, "onnx", "model_token.onnx"), new byte[TokenGraphBytes],
            TestContext.Current.CancellationToken);

        var options = new ImportOptions
        {
            License = new LicenseRecord("apache-2.0", "https://example.test/skytnt", null)
        };
        await store.ImportBundleAsync(name, root, options, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Imports a bundle shaped like the one above but holding a REAL graph - one of the checked-in ONNX
    /// fixtures - so that the managed engine can be run end to end with no interpreter anywhere.
    /// </summary>
    /// <param name="store">The store to import into.</param>
    /// <param name="directory">The temporary directory the folder is written inside.</param>
    /// <param name="name">The name to import it under.</param>
    /// <param name="fixtureName">The fixture to use as the graph.</param>
    /// <param name="graphName">
    /// The name the graph takes in the bundle. A fixture whose weights are in a file beside it has to keep
    /// its own name, because the name of that file is recorded inside the graph.
    /// </param>
    /// <returns>A task that completes when the bundle is in the store.</returns>
    private static async Task ImportRealGraphBundleAsync(
        ModelStore store,
        TempStoreDirectory directory,
        string name,
        string fixtureName = "matmul_k64_n8_fp32.onnx",
        string graphName = "model_base.onnx")
    {
        string root = Path.Combine(directory.DirectoryPath, "graph-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "onnx"));

        await File.WriteAllTextAsync(
            Path.Combine(root, "config.json"), "{\"architectures\":[\"MidiModel\"]}",
            TestContext.Current.CancellationToken);
        File.Copy(OnnxFixtureFiles.FullPath(fixtureName), Path.Combine(root, "onnx", graphName));
        if (File.Exists(OnnxFixtureFiles.FullPath(fixtureName + ".data")))
        {
            File.Copy(
                OnnxFixtureFiles.FullPath(fixtureName + ".data"),
                Path.Combine(root, "onnx", graphName + ".data"));
        }

        var options = new ImportOptions
        {
            License = new LicenseRecord("apache-2.0", "https://example.test/skytnt", null)
        };
        await store.ImportBundleAsync(name, root, options, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Imports a bundle with no graph in it at all, which is what a reduction has nothing to do with.
    /// </summary>
    /// <param name="store">The store to import into.</param>
    /// <param name="directory">The temporary directory the folder is written inside.</param>
    /// <returns>A task that completes when the bundle is in the store.</returns>
    private static async Task ImportCheckpointBundleAsync(ModelStore store, TempStoreDirectory directory)
    {
        string root = Path.Combine(directory.DirectoryPath, "checkpoint-source");
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(
            Path.Combine(root, "config.json"), "{\"architectures\":[\"LlamaForCausalLM\"]}",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "pytorch_model.bin"), "the weights, near enough for a test",
            TestContext.Current.CancellationToken);

        await store.ImportBundleAsync(CheckpointName, root, null, TestContext.Current.CancellationToken);
    }
}

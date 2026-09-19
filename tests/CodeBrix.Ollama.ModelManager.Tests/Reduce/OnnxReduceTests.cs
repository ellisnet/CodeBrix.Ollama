using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Ollama.Core;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers everything a reduction decides before any arithmetic happens: which engine will run, what the
/// derived bundle is called, which of a bundle's files are graphs, which are the weights of a graph,
/// what is carried through beside them, and what the bundle records about the run. None of it starts an
/// interpreter, so all of it runs on every machine.
/// </summary>
public sealed class OnnxReduceTests
{
    //The whole table of engine choices: every mode, with and without the shape-inference marker, with and
    //without a usable CPython. The rows that end in an exception are the two tests below it.
    [Theory]
    //Asked for outright: the caller's word, whatever the graph and the machine look like.
    [InlineData(ReduceEngine.Python, ReduceMode.DynamicInt8, false, false, ReduceEngine.Python)]
    [InlineData(ReduceEngine.Python, ReduceMode.WeightOnlyInt4, true, true, ReduceEngine.Python)]
    [InlineData(ReduceEngine.Python, ReduceMode.WeightOnlyInt8, false, true, ReduceEngine.Python)]
    [InlineData(ReduceEngine.Python, ReduceMode.PreprocessOnly, false, true, ReduceEngine.Python)]
    [InlineData(ReduceEngine.Managed, ReduceMode.DynamicInt8, false, true, ReduceEngine.Managed)]
    [InlineData(ReduceEngine.Managed, ReduceMode.WeightOnlyInt4, false, false, ReduceEngine.Managed)]
    [InlineData(ReduceEngine.Managed, ReduceMode.WeightOnlyInt8, false, false, ReduceEngine.Managed)]
    //Left to the library: the weight-only modes are managed whatever else is true.
    [InlineData(ReduceEngine.Auto, ReduceMode.WeightOnlyInt4, false, false, ReduceEngine.Managed)]
    [InlineData(ReduceEngine.Auto, ReduceMode.WeightOnlyInt4, true, true, ReduceEngine.Managed)]
    [InlineData(ReduceEngine.Auto, ReduceMode.WeightOnlyInt8, false, false, ReduceEngine.Managed)]
    [InlineData(ReduceEngine.Auto, ReduceMode.WeightOnlyInt8, true, true, ReduceEngine.Managed)]
    //Preparation is Python's, always.
    [InlineData(ReduceEngine.Auto, ReduceMode.PreprocessOnly, false, true, ReduceEngine.Python)]
    [InlineData(ReduceEngine.Auto, ReduceMode.PreprocessOnly, true, true, ReduceEngine.Python)]
    //Dynamic: the prepared graph is managed, the unprepared one is Python's.
    [InlineData(ReduceEngine.Auto, ReduceMode.DynamicInt8, true, true, ReduceEngine.Managed)]
    [InlineData(ReduceEngine.Auto, ReduceMode.DynamicInt8, true, false, ReduceEngine.Managed)]
    [InlineData(ReduceEngine.Auto, ReduceMode.DynamicInt8, false, true, ReduceEngine.Python)]
    public void ResolveEngine_answers_the_same_way_for_every_mode_marker_and_machine(
        ReduceEngine requested,
        ReduceMode mode,
        bool hasInferMarker,
        bool isPythonAvailable,
        ReduceEngine expected)
        => OnnxReduce.ResolveEngine(requested, mode, hasInferMarker, isPythonAvailable).Should().Be(expected);

    [Fact]
    public void ResolveEngine_with_Managed_refuses_the_mode_that_prepares_a_graph()
    {
        //Arrange
        Action act = () => OnnxReduce.ResolveEngine(
            ReduceEngine.Managed, ReduceMode.PreprocessOnly, false, true);

        //Act and assert
        act.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("shape inference");
    }

    [Fact]
    public void ResolveEngine_says_what_to_do_when_nothing_can_quantize_an_unprepared_graph()
    {
        //Arrange
        Action act = () => OnnxReduce.ResolveEngine(
            ReduceEngine.Auto, ReduceMode.DynamicInt8, false, false);

        //Act and assert
        act.Should().Throw<ModelManagerException>()
            .Which.Message.Should().Contain("ReduceMode.PreprocessOnly");
    }

    [Theory]
    [InlineData(ReduceEngine.Auto, ReduceMode.DynamicInt8, true)]
    [InlineData(ReduceEngine.Auto, ReduceMode.WeightOnlyInt4, false)]
    [InlineData(ReduceEngine.Auto, ReduceMode.WeightOnlyInt8, false)]
    [InlineData(ReduceEngine.Auto, ReduceMode.PreprocessOnly, false)]
    [InlineData(ReduceEngine.Managed, ReduceMode.DynamicInt8, false)]
    [InlineData(ReduceEngine.Python, ReduceMode.DynamicInt8, false)]
    public void NeedsInferMarker_reads_the_marker_only_where_it_decides_the_engine(
        ReduceEngine requested, ReduceMode mode, bool expected)
        => OnnxReduce.NeedsInferMarker(requested, mode).Should().Be(expected);

    [Theory]
    [InlineData(ReduceEngine.Auto, ReduceMode.DynamicInt8, false, true)]
    [InlineData(ReduceEngine.Auto, ReduceMode.DynamicInt8, true, false)]
    [InlineData(ReduceEngine.Auto, ReduceMode.WeightOnlyInt4, false, false)]
    [InlineData(ReduceEngine.Auto, ReduceMode.WeightOnlyInt8, false, false)]
    [InlineData(ReduceEngine.Managed, ReduceMode.DynamicInt8, false, false)]
    public void NeedsPythonAvailability_looks_for_an_interpreter_only_where_it_decides_the_engine(
        ReduceEngine requested, ReduceMode mode, bool hasInferMarker, bool expected)
        => OnnxReduce.NeedsPythonAvailability(requested, mode, hasInferMarker).Should().Be(expected);

    [Fact]
    public void ManagedToolVersion_is_the_version_of_this_library()
        => OnnxReduce.ManagedToolVersion.Should().Be(
            typeof(ModelStore).Assembly.GetName().Version.ToString());

    [Fact]
    public void ManagedTool_names_this_library()
        => OnnxReduce.ManagedTool.Should().Be("CodeBrix.Ollama.ModelManager");

    [Theory]
    [InlineData(ReduceMode.DynamicInt8, "int8")]
    [InlineData(ReduceMode.WeightOnlyInt8, "int8-weights")]
    [InlineData(ReduceMode.WeightOnlyInt4, "int4-weights")]
    [InlineData(ReduceMode.PreprocessOnly, "onnx-preprocessed")]
    public void TagFor_names_the_tag_a_mode_stores_its_output_under(ReduceMode mode, string expected)
        => OnnxReduce.TagFor(mode).Should().Be(expected);

    [Theory]
    [InlineData("latest", "int8", "int8")]
    [InlineData("LATEST", "int4-weights", "int4-weights")]
    [InlineData("", "int8-weights", "int8-weights")]
    [InlineData(null, "int8", "int8")]
    [InlineData("onnx", "int4-weights", "onnx-int4-weights")]
    [InlineData("onnx", "onnx-preprocessed", "onnx-preprocessed")]
    [InlineData("onnx-fp32", "int8", "onnx-fp32-int8")]
    [InlineData("onnx-only", "int8-weights", "onnx-only-int8-weights")]
    [InlineData("onnx-preprocessed", "int4-weights", "onnx-preprocessed-int4-weights")]
    public void AppendTag_adds_the_modes_tag_to_the_one_the_source_carries(
        string sourceTag, string suffix, string expected)
        => OnnxReduce.AppendTag(sourceTag, suffix).Should().Be(expected);

    [Fact]
    public void SelectFiles_with_nothing_named_takes_every_graph_in_manifest_order()
        => OnnxReduce.SelectFiles(Bundle(), null)
            .Should().Equal("onnx/model_base.onnx", "onnx/model_token.onnx");

    [Fact]
    public void SelectFiles_with_an_empty_list_takes_every_graph()
        => OnnxReduce.SelectFiles(Bundle(), Array.Empty<string>())
            .Should().Equal("onnx/model_base.onnx", "onnx/model_token.onnx");

    [Fact]
    public void SelectFiles_takes_the_graphs_the_caller_named()
        => OnnxReduce.SelectFiles(Bundle(), new[] { "onnx/model_token.onnx" })
            .Should().Equal("onnx/model_token.onnx");

    [Fact]
    public void SelectFiles_puts_what_the_caller_named_back_into_manifest_order()
        => OnnxReduce.SelectFiles(Bundle(), new[] { "onnx/model_token.onnx", "onnx/model_base.onnx" })
            .Should().Equal("onnx/model_base.onnx", "onnx/model_token.onnx");

    [Fact]
    public void SelectFiles_names_the_same_graph_once_however_often_it_is_asked_for()
        => OnnxReduce.SelectFiles(Bundle(), new[] { "onnx/model_base.onnx", "onnx/model_base.onnx" })
            .Should().Equal("onnx/model_base.onnx");

    [Fact]
    public void SelectFiles_reads_a_windows_style_path_as_the_bundle_spells_it()
        => OnnxReduce.SelectFiles(Bundle(), new[] { "onnx\\model_base.onnx" })
            .Should().Equal("onnx/model_base.onnx");

    [Fact]
    public void SelectFiles_with_a_file_the_bundle_does_not_hold_lists_the_ones_it_does()
    {
        //Arrange
        Action act = () => OnnxReduce.SelectFiles(Bundle(), new[] { "model.onnx" });

        //Act and assert
        act.Should().Throw<ModelManagerException>()
            .Which.Message.Should().Contain("onnx/model_base.onnx, onnx/model_token.onnx");
    }

    [Fact]
    public void SelectFiles_with_a_file_that_is_not_a_graph_is_refused()
    {
        //Arrange
        Action act = () => OnnxReduce.SelectFiles(Bundle(), new[] { "config.json" });

        //Act and assert
        act.Should().Throw<ModelManagerException>()
            .Which.Message.Should().Contain("no .onnx file called 'config.json'");
    }

    [Fact]
    public void SelectFiles_of_a_bundle_with_no_graph_says_to_export_one_first()
    {
        //Arrange
        var files = new[] { File("config.json", 10) };
        Action act = () => OnnxReduce.SelectFiles(files, null);

        //Act and assert
        act.Should().Throw<ModelManagerException>()
            .Which.Message.Should().Contain("ExportToOnnxAsync");
    }

    [Theory]
    [InlineData("model.onnx.data", "model.onnx", true)]
    [InlineData("model.onnx_data", "model.onnx", true)]
    [InlineData("onnx/model_base.onnx.data", "onnx/model_base.onnx", true)]
    [InlineData("model.onnx", "model.onnx", false)]
    [InlineData("model_token.onnx", "model.onnx", false)]
    [InlineData("config.json", "model.onnx", false)]
    [InlineData("", "model.onnx", false)]
    [InlineData("model.onnx.data", "", false)]
    public void IsExternalDataFor_knows_which_file_holds_a_graphs_weights(
        string path, string graphPath, bool expected)
        => OnnxReduce.IsExternalDataFor(path, graphPath).Should().Be(expected);

    [Fact]
    public void CompanionFiles_keeps_everything_the_reduction_does_not_write_itself()
    {
        //Arrange
        var files = new[]
        {
            File("README.md", 10),
            File("config.json", 20),
            File("tokenizer.json", 30),
            File("model.onnx", 40),
            File("model.onnx.data", 50),
            File("model.safetensors", 60)
        };

        //Act
        IReadOnlyList<ResolvedFile> kept = OnnxReduce.CompanionFiles(files, new[] { "model.onnx" });

        //Assert
        kept.Select(file => file.Name).Should().Equal("README.md", "config.json", "tokenizer.json");
    }

    [Fact]
    public void CompanionFiles_carries_a_graph_nobody_asked_to_reduce_through_unchanged()
    {
        //Act
        IReadOnlyList<ResolvedFile> kept = OnnxReduce.CompanionFiles(
            Bundle(), new[] { "onnx/model_base.onnx" });

        //Assert
        kept.Select(file => file.Name).Should().Equal(
            "README.md", "config.json", "onnx/model_token.onnx");
    }

    [Fact]
    public void CompanionFiles_with_no_files_keeps_nothing()
        => OnnxReduce.CompanionFiles(null, new[] { "model.onnx" }).Should().BeEmpty();

    [Theory]
    [InlineData(ReduceMode.DynamicInt8, true, true)]
    [InlineData(ReduceMode.DynamicInt8, false, false)]
    [InlineData(ReduceMode.WeightOnlyInt4, true, false)]
    [InlineData(ReduceMode.WeightOnlyInt8, true, false)]
    [InlineData(ReduceMode.PreprocessOnly, true, false)]
    public void PreprocessesFor_prepares_a_graph_only_for_the_mode_that_reads_the_shapes(
        ReduceMode mode, bool preprocess, bool expected)
        => OnnxReduce.PreprocessesFor(mode, new ReduceOptions { Preprocess = preprocess })
            .Should().Be(expected);

    [Theory]
    [InlineData(0L, false)]
    [InlineData(1610612736L, false)]
    [InlineData(1610612737L, true)]
    public void UseExternalData_writes_the_weights_beside_a_graph_that_is_nearly_too_large(
        long bytes, bool expected)
        => OnnxReduce.UseExternalData(bytes).Should().Be(expected);

    [Theory]
    //Nothing is at risk: one file, which is what the Python engine writes for the same graph.
    [InlineData(0L, 0L, false)]
    [InlineData(762_499_282L, 237_012_363L, false)]
    [InlineData(1_610_612_736L, 1_610_612_736L, false)]
    //The graph handed over is large enough that the Python engine would write the weights beside it.
    [InlineData(1_610_612_737L, 1024L, true)]
    [InlineData(4_000_000_000L, 900_000_000L, true)]
    //Only the quantized model is large: quantizing never grows a model, so this cannot happen in a
    //reduction - but a message that could not be built must never be attempted, whatever produced it.
    [InlineData(1024L, 2_500_000_000L, true)]
    public void UseExternalDataForManaged_writes_the_weights_beside_a_graph_that_could_not_be_one_message(
        long graphBytes, long quantizedBytes, bool expected)
        => OnnxReduce.UseExternalDataForManaged(graphBytes, quantizedBytes).Should().Be(expected);

    [Fact]
    public async Task MeasureModel_counts_every_tensors_bytes()
    {
        //Arrange
        OnnxModel model = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("matmul_k64_n8_fp32.onnx"), TestContext.Current.CancellationToken);

        //Act and assert
        //One weight of 64 by 8 single-precision values.
        OnnxReduce.MeasureModel(model).Should().Be(64L * 8L * 4L);
    }

    [Fact]
    public async Task HasExternalTensors_sees_the_weights_a_graph_keeps_beside_it()
    {
        //Arrange
        OnnxModel beside = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("external_gather_fp32.onnx"), TestContext.Current.CancellationToken);
        OnnxModel inside = await OnnxModel.ReadAsync(
            OnnxFixtureFiles.FullPath("gather_transpose_fp32.onnx"), TestContext.Current.CancellationToken);

        //Act and assert
        OnnxReduce.HasExternalTensors(beside).Should().BeTrue();
        OnnxReduce.HasExternalTensors(inside).Should().BeFalse();
    }

    [Theory]
    [InlineData(ReduceMode.WeightOnlyInt4, 4)]
    [InlineData(ReduceMode.WeightOnlyInt8, 8)]
    public void WeightOnlyOptionsFor_maps_this_librarys_options_onto_the_quantizers_own(
        ReduceMode mode, int expectedBits)
    {
        //Arrange
        var options = new ReduceOptions { BlockSize = 32, IsSymmetric = true, AccuracyLevel = 4 };

        //Act
        OnnxWeightOnlyQuantizationOptions mapped = OnnxReduce.WeightOnlyOptionsFor(mode, options);

        //Assert
        mapped.Bits.Should().Be(expectedBits);
        mapped.BlockSize.Should().Be(32);
        mapped.IsSymmetric.Should().BeTrue();
        mapped.AccuracyLevel.Should().Be(4);
        mapped.OpTypesToQuantize.Should().BeEquivalentTo(new[] { "MatMul" });
    }

    [Fact]
    public void DynamicOptionsFor_is_the_tools_own_defaults()
    {
        //Act
        OnnxDynamicQuantizationOptions mapped = OnnxReduce.DynamicOptionsFor();

        //Assert
        mapped.WeightType.Should().Be(OnnxTensorDataType.Int8);
        mapped.PerChannel.Should().BeFalse();
        mapped.ReduceRange.Should().BeFalse();
        mapped.OpTypesToQuantize.Should().BeEquivalentTo(new[] { "MatMul", "Gemm" });
    }

    [Fact]
    public void MeasureGraph_counts_the_graph_and_the_file_of_weights_beside_it()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        string graph = Path.Combine(directory.DirectoryPath, "model.onnx");
        System.IO.File.WriteAllBytes(graph, new byte[7]);
        System.IO.File.WriteAllBytes(graph + ".data", new byte[11]);
        System.IO.File.WriteAllBytes(Path.Combine(directory.DirectoryPath, "other.onnx"), new byte[13]);

        //Act and assert
        OnnxReduce.MeasureGraph(graph).Should().Be(18L);
    }

    [Fact]
    public void ExternalDataPaths_finds_the_file_of_weights_beside_a_graph()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        string graph = Path.Combine(directory.DirectoryPath, "model.onnx");
        System.IO.File.WriteAllBytes(graph, new byte[3]);
        System.IO.File.WriteAllBytes(graph + ".data", new byte[5]);
        System.IO.File.WriteAllBytes(Path.Combine(directory.DirectoryPath, "other.onnx"), new byte[7]);

        //Act and assert
        OnnxReduce.ExternalDataPaths(graph).Should().Equal(graph + ".data");
    }

    [Fact]
    public void UnlinkExternalData_gives_a_graphs_weights_a_file_of_their_own()
    {
        //Arrange
        //The ONNX package refuses to read a file of weights that has more than one hard link, and this
        //library lays a bundle out by hard-linking it, so the weights of a graph about to be read have
        //to stop being a link. A file system without hard links has nothing to prove here.
        using var directory = new TempStoreDirectory();
        string shared = Path.Combine(directory.DirectoryPath, "blob");
        string graph = Path.Combine(directory.DirectoryPath, "model.onnx");
        string weights = graph + ".data";
        System.IO.File.WriteAllBytes(shared, new byte[] { 1, 2, 3, 4 });
        System.IO.File.WriteAllBytes(graph, new byte[] { 9 });
        if (!HardLink.TryCreate(shared, weights))
        {
            return;
        }

        //Act
        OnnxReduce.UnlinkExternalData(graph);

        //Assert
        System.IO.File.ReadAllBytes(weights).Should().Equal(new byte[] { 1, 2, 3, 4 });
        System.IO.File.WriteAllBytes(shared, new byte[] { 5, 6, 7, 8 });
        System.IO.File.ReadAllBytes(weights).Should().Equal(
            new byte[] { 1, 2, 3, 4 },
            "the copy must no longer share its content with the blob it was linked to");
    }

    [Fact]
    public void UnlinkExternalData_of_a_graph_with_no_weights_beside_it_does_nothing()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        string graph = Path.Combine(directory.DirectoryPath, "model.onnx");
        System.IO.File.WriteAllBytes(graph, new byte[] { 1, 2, 3 });

        //Act
        OnnxReduce.UnlinkExternalData(graph);

        //Assert
        System.IO.File.ReadAllBytes(graph).Should().Equal(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void MeasureGraph_of_a_file_that_is_not_there_measures_nothing()
        => OnnxReduce.MeasureGraph(Path.Combine(Path.GetTempPath(), "no-such-model.onnx"))
            .Should().Be(0L);

    [Fact]
    public void SettingsFor_records_what_a_weight_only_run_was_asked_for()
    {
        //Arrange
        var options = new ReduceOptions
        {
            Mode = ReduceMode.WeightOnlyInt4,
            BlockSize = 32,
            IsSymmetric = true,
            AccuracyLevel = 4,
            Engine = ReduceEngine.Auto
        };

        //Act
        IReadOnlyDictionary<string, string> settings = OnnxReduce.SettingsFor(
            ReduceMode.WeightOnlyInt4, ReduceEngine.Python, options, new[] { "model.onnx" });

        //Assert
        settings["mode"].Should().Be("WeightOnlyInt4");
        settings["engine"].Should().Be("Python");
        settings["requestedEngine"].Should().Be("Auto");
        settings["preprocess"].Should().Be("false");
        settings["blockSize"].Should().Be("32");
        settings["isSymmetric"].Should().Be("true");
        settings["accuracyLevel"].Should().Be("4");
        settings["files"].Should().Be("model.onnx");
    }

    [Fact]
    public void SettingsFor_leaves_the_block_wise_entries_empty_for_a_dynamic_run()
    {
        //Act
        IReadOnlyDictionary<string, string> settings = OnnxReduce.SettingsFor(
            ReduceMode.DynamicInt8, ReduceEngine.Python, new ReduceOptions(),
            new[] { "a.onnx", "b.onnx" });

        //Assert
        settings["mode"].Should().Be("DynamicInt8");
        settings["preprocess"].Should().Be("true");
        settings["blockSize"].Should().BeEmpty();
        settings["isSymmetric"].Should().BeEmpty();
        settings["accuracyLevel"].Should().BeEmpty();
        settings["files"].Should().Be("a.onnx,b.onnx");
    }

    [Fact]
    public void SettingsFor_leaves_the_accuracy_level_empty_when_the_runtime_is_to_choose()
        => OnnxReduce.SettingsFor(
                ReduceMode.WeightOnlyInt8, ReduceEngine.Python, new ReduceOptions(),
                new[] { "model.onnx" })["accuracyLevel"]
            .Should().BeEmpty();

    /// <summary>
    /// The files of a bundle shaped like the one the live tests reduce: a model card, a configuration
    /// and two graphs in a folder of their own.
    /// </summary>
    /// <returns>The files, in manifest order.</returns>
    private static IReadOnlyList<ResolvedFile> Bundle()
        => new[]
        {
            File("README.md", 100),
            File("config.json", 200),
            File("onnx/model_base.onnx", 800),
            File("onnx/model_token.onnx", 400)
        };

    /// <summary>
    /// One file of a bundle, with a digest that is unique to its name.
    /// </summary>
    /// <param name="name">The publisher's relative path.</param>
    /// <param name="size">The size in bytes.</param>
    /// <returns>The resolved file.</returns>
    private static ResolvedFile File(string name, long size)
        => new ResolvedFile(name, "/blobs/" + name.Replace('/', '-'), size, "sha256:" + name.GetHashCode());
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The three ways a graph is loaded, and every graph the engine is required to turn away.
/// </summary>
/// <remarks>
/// The refusals matter as much as the loads. A graph that asks for an operator, an operator set, an element
/// type or an attribute this engine does not implement has to say so WHEN IT IS LOADED, naming the node -
/// not half way through a generation, and never by quietly computing something else.
/// </remarks>
public sealed class OnnxModelTests
{
    /// <summary>A graph loads from the path of its own file.</summary>
    [Fact]
    public async Task LoadAsync_reads_a_graph_file()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("subgraph_feed_forward");

        //Act
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);

        //Assert
        model.Metadata.Inputs.Should().HaveCount(1);
    }

    /// <summary>A graph loads out of a bundle directory by the name the publisher gave it.</summary>
    [Fact]
    public async Task LoadFromDirectoryAsync_reads_a_graph_out_of_a_bundle()
    {
        //Arrange
        var directory = Path.Combine(OnnxFixtures.Directory, "subgraph_rms_norm");

        //Act
        await using var model = await OnnxModel.LoadFromDirectoryAsync(
            directory, "model.onnx", null, TestContext.Current.CancellationToken);

        //Assert
        model.Metadata.Operators.Should().Contain("ReduceMean");
    }

    /// <summary>A graph loads from a set of (logical file name to path) pairs.</summary>
    [Fact]
    public async Task LoadFromFilesAsync_reads_a_graph_named_among_a_set_of_paths()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("subgraph_rms_norm");
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["onnx/model_base.onnx"] = fixture.ModelPath,
        };

        //Act
        await using var model = await OnnxModel.LoadFromFilesAsync(
            files, "onnx/model_base.onnx", null, TestContext.Current.CancellationToken);

        //Assert
        model.Metadata.Operators.Should().Contain("Sqrt");
    }

    /// <summary>A graph whose weights are in a side file beside it loads and computes what the oracle says.</summary>
    [Fact]
    public async Task LoadFromDirectoryAsync_follows_a_side_file()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("external_weights");
        var directory = Path.Combine(OnnxFixtures.Directory, "external_weights");

        //Act
        await using var model = await OnnxModel.LoadFromDirectoryAsync(
            directory, "model.onnx", null, TestContext.Current.CancellationToken);
        var produced = model.Run(fixture.Inputs);

        //Assert
        OnnxComparison.Compare(fixture.Expected["y"], produced["y"])
            .RelativeToLargest.Should().BeLessThan(1e-5);
    }

    /// <summary>
    /// A graph whose side file is held under a name of its own - as a content store holds it, under a digest -
    /// loads when the caller says which file is which, and nothing is copied into a directory first.
    /// </summary>
    [Fact]
    public async Task LoadFromFilesAsync_follows_a_side_file_held_under_another_name()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("external_weights");
        var source = Path.Combine(OnnxFixtures.Directory, "external_weights");
        using var scratch = new TempScratchDirectory();
        var graph = scratch.Combine("sha256-11119999");
        var weights = scratch.Combine("sha256-22228888");
        File.Copy(Path.Combine(source, "model.onnx"), graph);
        File.Copy(Path.Combine(source, "model.onnx.data"), weights);
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["model.onnx"] = graph,
            ["model.onnx.data"] = weights,
        };

        //Act
        await using var model = await OnnxModel.LoadFromFilesAsync(
            files, "model.onnx", null, TestContext.Current.CancellationToken);
        var produced = model.Run(fixture.Inputs);

        //Assert
        OnnxComparison.Compare(fixture.Expected["y"], produced["y"])
            .RelativeToLargest.Should().BeLessThan(1e-5);
    }

    /// <summary>A side file the caller did not name is refused, and the missing file is named.</summary>
    [Fact]
    public async Task LoadFromFilesAsync_without_the_side_file_says_which_file_is_missing()
    {
        //Arrange
        var source = Path.Combine(OnnxFixtures.Directory, "external_weights");
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["model.onnx"] = Path.Combine(source, "model.onnx"),
        };

        //Act
        Func<Task> act = async () => await OnnxModel.LoadFromFilesAsync(
            files, "model.onnx", null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelLoadException>())
            .Which.Message.Should().Contain("model.onnx.data");
    }

    /// <summary>Every graph the engine has to turn away is turned away, and the message says why.</summary>
    /// <param name="name">The graph's name.</param>
    /// <param name="expected">Text the refusal has to carry.</param>
    [Theory]
    [MemberData(nameof(OnnxFixtures.AllRefusals), MemberType = typeof(OnnxFixtures))]
    public async Task LoadAsync_refuses_a_graph_it_cannot_run(string name, string expected)
    {
        //Arrange
        var path = OnnxFixtures.RefusalPath(name);

        //Act
        Func<Task> act = async () => await OnnxModel.LoadAsync(
            path, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelLoadException>())
            .Which.Message.Should().Contain(expected);
    }

    /// <summary>A file that is not a graph at all does not bring the process down.</summary>
    [Fact]
    public async Task LoadAsync_with_rubbish_throws_a_load_exception()
    {
        //Arrange
        using var scratch = new TempScratchDirectory();
        var path = scratch.Combine("not-a-model.onnx");
        var rubbish = new byte[4096];
        new Random(20260918).NextBytes(rubbish);
        await File.WriteAllBytesAsync(path, rubbish, TestContext.Current.CancellationToken);

        //Act
        Func<Task> act = async () => await OnnxModel.LoadAsync(
            path, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelRunnerException>();
    }

    /// <summary>A file that is not there is named.</summary>
    [Fact]
    public async Task LoadAsync_with_a_missing_file_names_it()
    {
        //Arrange
        var path = Path.Combine(OnnxFixtures.Directory, "no-such-graph.onnx");

        //Act
        Func<Task> act = async () => await OnnxModel.LoadAsync(
            path, null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ModelLoadException>())
            .Which.Message.Should().Contain("no-such-graph.onnx");
    }

    /// <summary>An empty path is an argument error rather than a load failure.</summary>
    [Fact]
    public async Task LoadAsync_without_a_path_throws()
    {
        //Act
        Func<Task> act = async () => await OnnxModel.LoadAsync(
            " ", null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>A file set that does not name the graph itself says so, and lists what it does name.</summary>
    [Fact]
    public async Task LoadFromFilesAsync_without_the_graph_throws()
    {
        //Arrange
        var files = new Dictionary<string, string>(StringComparer.Ordinal) { ["config.json"] = "/tmp/x" };

        //Act
        Func<Task> act = async () => await OnnxModel.LoadFromFilesAsync(
            files, "model.onnx", null, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("config.json");
    }

    /// <summary>A null file set is an argument error.</summary>
    [Fact]
    public async Task LoadFromFilesAsync_with_null_files_throws()
    {
        //Act
        Func<Task> act = async () => await OnnxModel.LoadFromFilesAsync(
            null, "model.onnx", null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>A thread count below one is refused rather than silently corrected.</summary>
    [Fact]
    public async Task LoadAsync_with_no_threads_throws()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");
        var options = new OnnxRunnerOptions { Threads = 0 };

        //Act
        Func<Task> act = async () => await OnnxModel.LoadAsync(
            fixture.ModelPath, options, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>A thread cap below one is refused, and the message names the property to fix.</summary>
    /// <param name="cap">The cap that cannot be met.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task LoadAsync_with_a_thread_cap_below_one_throws(int cap)
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");
        var options = new OnnxRunnerOptions { MaxThreads = cap };

        //Act
        Func<Task> act = async () => await OnnxModel.LoadAsync(
            fixture.ModelPath, options, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ArgumentOutOfRangeException>())
            .Which.Message.Should().Contain("MaxThreads");
    }

    /// <summary>A cap of one leaves the graph running on one thread.</summary>
    [Fact]
    public async Task LoadAsync_with_a_thread_cap_bounds_the_automatic_count()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");
        var options = new OnnxRunnerOptions { MaxThreads = 1 };

        //Act
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, options, TestContext.Current.CancellationToken);

        //Assert
        model.Options.Threads.Should().Be(1);
        model.Options.MaxThreads.Should().Be(1);
    }

    /// <summary>A stated thread count wins over a cap, and a cap larger than the machine changes nothing.</summary>
    [Fact]
    public async Task LoadAsync_lets_a_stated_thread_count_win_over_the_cap()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");

        //Act
        await using var stated = await OnnxModel.LoadAsync(
            fixture.ModelPath, new OnnxRunnerOptions { Threads = 3, MaxThreads = 1 },
            TestContext.Current.CancellationToken);
        await using var generous = await OnnxModel.LoadAsync(
            fixture.ModelPath, new OnnxRunnerOptions { MaxThreads = 4096 },
            TestContext.Current.CancellationToken);
        await using var uncapped = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);

        //Assert
        stated.Options.Threads.Should().Be(3);
        generous.Options.Threads.Should().Be(uncapped.Options.Threads);
    }

    /// <summary>The options a caller hands in are copied, so a later edit cannot change a loaded model.</summary>
    [Fact]
    public async Task LoadAsync_copies_the_options()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");
        var options = new OnnxRunnerOptions { Threads = 2 };

        //Act
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, options, TestContext.Current.CancellationToken);
        options.Threads = 9;

        //Assert
        model.Options.Threads.Should().Be(2);
    }

    /// <summary>A directory that is not there is a load failure naming the file it looked for.</summary>
    [Fact]
    public async Task LoadFromDirectoryAsync_with_a_missing_bundle_throws()
    {
        //Act
        Func<Task> act = async () => await OnnxModel.LoadFromDirectoryAsync(
            Path.Combine(OnnxFixtures.Directory, "no-such-bundle"), "model.onnx", null,
            TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelLoadException>();
    }

    /// <summary>An empty bundle directory is an argument error.</summary>
    [Fact]
    public async Task LoadFromDirectoryAsync_without_a_directory_throws()
    {
        //Act
        Func<Task> act = async () => await OnnxModel.LoadFromDirectoryAsync(
            string.Empty, "model.onnx", null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>An empty graph file name is an argument error.</summary>
    [Fact]
    public async Task LoadFromDirectoryAsync_without_a_file_name_throws()
    {
        //Act
        Func<Task> act = async () => await OnnxModel.LoadFromDirectoryAsync(
            OnnxFixtures.Directory, string.Empty, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}

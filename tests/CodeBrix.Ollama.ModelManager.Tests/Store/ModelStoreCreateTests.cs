using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers creating a model from a Modelfile: importing a GGUF file from disk, inheriting from a model
/// already in the store, and the way TEMPLATE, SYSTEM, PARAMETER, LICENSE, MESSAGE, ADAPTER and DRAFT
/// lines are applied.
/// </summary>
public sealed class ModelStoreCreateTests
{
    [Fact]
    public async Task CreateAsync_from_a_local_gguf_file_writes_the_model_and_the_modelfile_layers()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        string sourceDirectory = CreateSourceDirectory(directory);
        string modelPath = WriteModelGguf(sourceDirectory, "model.gguf");
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        Modelfile modelfile = Modelfile.Parse(
            "FROM model.gguf\n"
            + "TEMPLATE {{ .Prompt }}\n"
            + "SYSTEM You are a created model.\n"
            + "PARAMETER temperature 0.25\n"
            + "PARAMETER stop <|end|>\n"
            + "LICENSE Apache-2.0\n"
            + "MESSAGE user hello\n");

        //Act
        await store.CreateAsync(
            "created:latest",
            modelfile,
            new CreateOptions { BaseDirectory = sourceDirectory },
            TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync("created:latest", TestContext.Current.CancellationToken);
        MediaTypesOf(info).Should().Equal(new List<string>
        {
            MediaTypes.Model,
            MediaTypes.Template,
            MediaTypes.System,
            MediaTypes.License,
            MediaTypes.Params,
            MediaTypes.Messages
        });
        info.Config.ModelFormat.Should().Be("gguf");
        info.Config.ModelFamily.Should().Be("llama");
        info.Config.ModelFamilies.Should().Equal(new List<string> { "llama" });
        info.Config.ModelType.Should().Be("192");
        info.Config.FileType.Should().Be("Q8_0");
        info.Template.Should().Be("{{ .Prompt }}");
        info.System.Should().Be("You are a created model.");
        info.Parameters.Temperature.Should().Be(0.25f);
        info.Parameters.Stop.Should().Equal(new List<string> { "<|end|>" });
        info.Licenses.Should().Equal(new List<string> { "Apache-2.0" });
        info.Messages.Should().HaveCount(1);
        info.Messages[0].Content.Should().Be("hello");
        File.Exists(modelPath).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_from_an_existing_model_overrides_the_template_and_merges_the_parameters()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);
        Modelfile modelfile = Modelfile.Parse(
            "FROM test:latest\n"
            + "TEMPLATE a brand new template\n"
            + "PARAMETER temperature 0.9\n"
            + "PARAMETER top_k 40\n"
            + "LICENSE Extra license\n");

        //Act
        await store.CreateAsync("derived:latest", modelfile, null, TestContext.Current.CancellationToken);

        //Assert
        ModelInfo info = await store.ShowAsync("derived:latest", TestContext.Current.CancellationToken);
        info.Template.Should().Be("a brand new template");
        info.System.Should().Be(builder.SystemPrompt);
        info.Parameters.Temperature.Should().Be(0.9f);
        info.Parameters.TopK.Should().Be(40);
        info.Parameters.Stop.Should().Equal(new List<string> { "</s>" });
        info.Licenses.Should().Equal(new List<string> { "MIT", "Extra license" });
        info.Config.ModelFamily.Should().Be("llama");
        info.Manifest.Layers[0].From.Should().Be("registry.test/library/test:latest");
    }

    [Fact]
    public async Task CreateAsync_from_an_existing_model_shares_the_model_blob()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        var builder = new FakeModelBuilder();
        builder.Build(handler);
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        await ModelStorePullTests.CollectStatusesAsync(store, builder.Reference);
        Modelfile modelfile = Modelfile.Parse("FROM test:latest\nSYSTEM a different system prompt\n");

        //Act
        await store.CreateAsync("derived:latest", modelfile, null, TestContext.Current.CancellationToken);

        //Assert
        ResolvedModel resolved = await store.ResolveAsync("derived:latest", TestContext.Current.CancellationToken);
        resolved.ModelPath.Should().Be(directory.Paths.GetBlobPath(builder.ModelDigest));
        resolved.System.Should().Be("a different system prompt");
    }

    [Fact]
    public async Task CreateAsync_when_the_name_already_exists_prunes_the_replaced_layers()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        string sourceDirectory = CreateSourceDirectory(directory);
        WriteModelGguf(sourceDirectory, "model.gguf");
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        var options = new CreateOptions { BaseDirectory = sourceDirectory };
        await store.CreateAsync(
            "created:latest",
            Modelfile.Parse("FROM model.gguf\nTEMPLATE the first template\n"),
            options,
            TestContext.Current.CancellationToken);
        ResolvedModel first = await store.ResolveAsync("created:latest", TestContext.Current.CancellationToken);
        string firstTemplateBlob = TemplateBlobPath(directory, "created");
        string modelBlob = first.ModelPath;

        //Act
        await store.CreateAsync(
            "created:latest",
            Modelfile.Parse("FROM model.gguf\nTEMPLATE the second template\n"),
            options,
            TestContext.Current.CancellationToken);

        //Assert
        File.Exists(firstTemplateBlob).Should().BeFalse();
        File.Exists(modelBlob).Should().BeTrue();
        ModelInfo info = await store.ShowAsync("created:latest", TestContext.Current.CancellationToken);
        info.Template.Should().Be("the second template");
    }

    [Fact]
    public async Task CreateAsync_from_something_that_is_neither_file_nor_model_throws_model_not_found()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        Modelfile modelfile = Modelfile.Parse("FROM nowhere-at-all\n");

        //Act
        Func<Task> act = () => store.CreateAsync(
            "created:latest",
            modelfile,
            new CreateOptions { BaseDirectory = directory.DirectoryPath },
            TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ModelNotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_with_an_adapter_that_is_not_one_throws_gguf_format()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        string sourceDirectory = CreateSourceDirectory(directory);
        WriteModelGguf(sourceDirectory, "model.gguf");
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        Modelfile modelfile = Modelfile.Parse("FROM model.gguf\nADAPTER model.gguf\n");

        //Act
        Func<Task> act = () => store.CreateAsync(
            "created:latest",
            modelfile,
            new CreateOptions { BaseDirectory = sourceDirectory },
            TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<GgufFormatException>();
    }

    [Fact]
    public async Task CreateAsync_with_a_draft_line_throws_because_draft_is_not_supported()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        string sourceDirectory = CreateSourceDirectory(directory);
        WriteModelGguf(sourceDirectory, "model.gguf");
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));
        Modelfile modelfile = Modelfile.Parse("FROM model.gguf\nDRAFT draft.gguf\n");

        //Act
        Exception captured = null;
        try
        {
            await store.CreateAsync(
                "created:latest",
                modelfile,
                new CreateOptions { BaseDirectory = sourceDirectory },
                TestContext.Current.CancellationToken);
        }
        catch (Exception exception)
        {
            captured = exception;
        }

        //Assert
        captured.Should().BeOfType<ModelManagerException>();
        captured.Message.Should().Contain("DRAFT");
    }

    [Fact]
    public async Task CreateAsync_with_a_null_modelfile_throws_argument_null()
    {
        //Arrange
        using var directory = new TempStoreDirectory();
        using var handler = new FakeRegistryHandler();
        using var store = new ModelStore(ModelStorePullTests.CreateOptions(handler, directory));

        //Act
        Func<Task> act = () => store.CreateAsync(
            "created:latest", null, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// Creates the directory a create test keeps its source files in, which is inside the test's own
    /// temporary directory and never the store's blobs or manifests.
    /// </summary>
    /// <param name="directory">The temporary store directory.</param>
    /// <returns>The source directory path.</returns>
    private static string CreateSourceDirectory(TempStoreDirectory directory)
    {
        string path = Path.Combine(directory.DirectoryPath, "source");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Writes a small model GGUF into the source directory.
    /// </summary>
    /// <param name="sourceDirectory">Where to write it.</param>
    /// <param name="fileName">The file name to give it.</param>
    /// <returns>The path that was written.</returns>
    private static string WriteModelGguf(string sourceDirectory, string fileName)
    {
        string path = Path.Combine(sourceDirectory, fileName);
        File.WriteAllBytes(path, new FakeModelBuilder().BuildModelGguf());
        return path;
    }

    /// <summary>
    /// The blob path of a created model's template layer.
    /// </summary>
    /// <param name="directory">The temporary store directory.</param>
    /// <param name="model">The model part of the name.</param>
    /// <returns>The blob path.</returns>
    private static string TemplateBlobPath(TempStoreDirectory directory, string model)
    {
        string manifestPath = Path.Combine(
            directory.DirectoryPath, "manifests", "registry.test", "library", model, "latest");
        ModelManifest manifest = ModelManagerJson.Deserialize<ModelManifest>(File.ReadAllBytes(manifestPath));
        ModelLayer template = manifest.Layers.First(layer => layer.MediaType == MediaTypes.Template);
        return directory.Paths.GetBlobPath(template.Digest);
    }

    /// <summary>
    /// The media types of a model's layers, in manifest order.
    /// </summary>
    /// <param name="info">The described model.</param>
    /// <returns>The media types.</returns>
    private static IReadOnlyList<string> MediaTypesOf(ModelInfo info)
    {
        return info.Manifest.Layers.Select(layer => layer.MediaType).ToList();
    }
}

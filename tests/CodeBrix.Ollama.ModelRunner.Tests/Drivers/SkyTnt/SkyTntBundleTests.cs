using System;
using System.Collections.Generic;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// What a bundle says about itself, and what is refused when it says something else.
/// </summary>
/// <remarks>
/// THE BUNDLE CHOOSES THE DRIVER, which is what keeps the engine itself free of any knowledge of one
/// publisher's model - so everything a bundle could say that this driver cannot generate for has to be
/// refused here, by name, rather than found out at the first step of a generation.
/// </remarks>
public sealed class SkyTntBundleTests
{
    /// <summary>The published bundle's description is read as it stands.</summary>
    [Fact]
    public void FromDirectory_reads_what_the_bundle_says_about_itself()
    {
        //Arrange and act
        SkyTntBundle bundle = SkyTntBundle.FromDirectory(SkyTntFixtures.TinyModelDirectory);

        //Assert
        bundle.Architecture.Should().Be("MIDIModel");
        bundle.BaseGraph.Should().Be("onnx/model_base.onnx");
        bundle.TokenGraph.Should().Be("onnx/model_token.onnx");
        bundle.Tokenizer.Version.Should().Be("v2");
        bundle.Tokenizer.OptimiseMidi.Should().BeTrue();
        bundle.Tokenizer.VocabularySize.Should().Be(3406);
        bundle.Tokenizer.Events.Should().HaveCount(6);
        bundle.Tokenizer.EventParameters.Should().HaveCount(15);
    }

    /// <summary>The same bundle held under other names reads the same.</summary>
    [Fact]
    public void FromFiles_reads_a_bundle_held_under_other_names()
    {
        //Arrange and act
        SkyTntBundle bundle = SkyTntBundle.FromFiles(SkyTntFixtures.TinyModelFiles());

        //Assert
        bundle.Architecture.Should().Be("MIDIModel");
        bundle.BaseGraph.Should().Be("onnx/model_base.onnx");
    }

    /// <summary>A directory that is not there is refused.</summary>
    [Fact]
    public void FromDirectory_of_a_directory_that_is_not_there_is_refused()
    {
        //Arrange
        Action act = () => SkyTntBundle.FromDirectory(
            Path.Combine(SkyTntFixtures.Directory, "no-such-bundle"));

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("no bundle directory");
    }

    /// <summary>A directory with no description in it is refused.</summary>
    [Fact]
    public void FromDirectory_without_a_configuration_is_refused()
    {
        //Arrange
        using SkyTntScratchBundle scratch = new SkyTntScratchBundle();
        File.Delete(Path.Combine(scratch.DirectoryPath, "config.json"));
        Action act = () => SkyTntBundle.FromDirectory(scratch.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("config.json");
    }

    /// <summary>A description that is not a model of this family is refused by name.</summary>
    [Fact]
    public void FromDirectory_of_another_architecture_is_refused()
    {
        //Arrange
        using SkyTntScratchBundle scratch = new SkyTntScratchBundle();
        scratch.WriteConfiguration("{\"architectures\":[\"LlamaForCausalLM\"],\"model_type\":\"llama\"}");
        Action act = () => SkyTntBundle.FromDirectory(scratch.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("LlamaForCausalLM");
    }

    /// <summary>A description naming this family only by its model type is accepted.</summary>
    [Fact]
    public void FromDirectory_of_a_bundle_naming_only_its_model_type_is_accepted()
    {
        //Arrange
        using SkyTntScratchBundle scratch = new SkyTntScratchBundle();
        scratch.WriteConfiguration(
            "{\"model_type\":\"midi_model\",\"tokenizer\":" + scratch.TokenizerBlock() + "}");

        //Act
        SkyTntBundle bundle = SkyTntBundle.FromDirectory(scratch.DirectoryPath);

        //Assert
        bundle.Tokenizer.Version.Should().Be("v2");
    }

    /// <summary>A description with no tokenizer block is refused.</summary>
    [Fact]
    public void FromDirectory_without_a_tokenizer_block_is_refused()
    {
        //Arrange
        using SkyTntScratchBundle scratch = new SkyTntScratchBundle();
        scratch.WriteConfiguration("{\"architectures\":[\"MIDIModel\"]}");
        Action act = () => SkyTntBundle.FromDirectory(scratch.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("tokenizer block");
    }

    /// <summary>A description that is not valid JSON is refused.</summary>
    [Fact]
    public void FromDirectory_of_a_broken_configuration_is_refused()
    {
        //Arrange
        using SkyTntScratchBundle scratch = new SkyTntScratchBundle();
        scratch.WriteConfiguration("{\"architectures\": [");
        Action act = () => SkyTntBundle.FromDirectory(scratch.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("valid JSON");
    }

    /// <summary>A bundle missing one of the two graphs is refused, saying which.</summary>
    [Fact]
    public void FromDirectory_without_the_token_graph_is_refused()
    {
        //Arrange
        using SkyTntScratchBundle scratch = new SkyTntScratchBundle();
        File.Delete(Path.Combine(scratch.DirectoryPath, "onnx", "model_token.onnx"));
        Action act = () => SkyTntBundle.FromDirectory(scratch.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("token graph");
    }

    /// <summary>A set of files with no description among them is refused.</summary>
    [Fact]
    public void FromFiles_without_a_configuration_is_refused()
    {
        //Arrange
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["onnx/model_base.onnx"] = Path.Combine(SkyTntFixtures.TinyModelDirectory, "onnx", "model_base.onnx"),
        };
        Action act = () => SkyTntBundle.FromFiles(files);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("config.json");
    }

    /// <summary>A set of files whose description is not on disk is refused.</summary>
    [Fact]
    public void FromFiles_whose_configuration_is_not_there_is_refused()
    {
        //Arrange
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["config.json"] = Path.Combine(SkyTntFixtures.Directory, "no-such-file.json"),
        };
        Action act = () => SkyTntBundle.FromFiles(files);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("no file at");
    }

    /// <summary>A graph named something else is still found when its name says which of the two it is.</summary>
    [Fact]
    public void FromDirectory_finds_a_graph_whose_name_says_which_of_the_two_it_is()
    {
        //Arrange
        using SkyTntScratchBundle scratch = new SkyTntScratchBundle();
        string onnx = Path.Combine(scratch.DirectoryPath, "onnx");
        File.Move(Path.Combine(onnx, "model_base.onnx"), Path.Combine(onnx, "decoder_base_fp32.onnx"));
        File.Move(Path.Combine(onnx, "model_token.onnx"), Path.Combine(onnx, "decoder_token_fp32.onnx"));

        //Act
        SkyTntBundle bundle = SkyTntBundle.FromDirectory(scratch.DirectoryPath);

        //Assert
        bundle.BaseGraph.Should().Be("onnx/decoder_base_fp32.onnx");
        bundle.TokenGraph.Should().Be("onnx/decoder_token_fp32.onnx");
    }
}

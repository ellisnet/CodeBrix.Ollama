using System;
using System.Collections.Generic;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// What a bundle's generation configuration says about itself, and everything it is refused for.
/// </summary>
public sealed class CausalLmBundleTests
{
    /// <summary>The tiny bundle reads back as what it is.</summary>
    [Fact]
    public void FromDirectory_reads_what_the_bundle_says()
    {
        //Act
        CausalLmBundle bundle = CausalLmBundle.FromDirectory(CausalLmFixtures.TinyBundleDirectory);

        //Assert
        bundle.Architecture.Should().Be("tinyllama");
        bundle.Decoder.FileName.Should().Be("model.onnx");
        bundle.Decoder.LayerCount.Should().Be(2);
        bundle.Decoder.HeadCount.Should().Be(4);
        bundle.Decoder.KeyValueHeadCount.Should().Be(2);
        bundle.Decoder.HeadSize.Should().Be(8);
        bundle.ContextLength.Should().Be(64);
        bundle.BeginningOfSequence.Should().Be(2);
        bundle.EndOfSequence.Should().Equal(3);
        bundle.Padding.Should().Be(0);
    }

    /// <summary>A set of (name to path) pairs reads back the same way a directory does.</summary>
    [Fact]
    public void FromFiles_reads_what_the_bundle_says()
    {
        //Act
        CausalLmBundle bundle = CausalLmBundle.FromFiles(CausalLmFixtures.TinyBundleFiles());

        //Assert
        bundle.Decoder.FileName.Should().Be("model.onnx");
        bundle.FindFile("model.onnx").Should().NotBeNull();
    }

    /// <summary>A layer's cache tensors are named by the patterns the configuration states.</summary>
    [Fact]
    public void CacheNames_uses_the_patterns_the_configuration_states()
    {
        //Arrange
        CausalLmBundle bundle = CausalLmBundle.FromDirectory(CausalLmFixtures.TinyBundleDirectory);

        //Act
        HashSet<string> names = bundle.Decoder.CacheNames();

        //Assert
        names.Should().Contain("past_key_values.0.key");
        names.Should().Contain("present.1.value");
        names.Should().HaveCount(8);
    }

    /// <summary>A directory that is not there is refused by its path.</summary>
    [Fact]
    public void FromDirectory_with_no_such_directory_refuses()
    {
        //Arrange
        Action act = () => CausalLmBundle.FromDirectory(
            Path.Combine(AppContext.BaseDirectory, "no-such-bundle"));

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("no bundle directory");
    }

    /// <summary>A bundle with no generation configuration is refused by the name of the file it lacks.</summary>
    [Fact]
    public void FromDirectory_with_no_configuration_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Remove("genai_config.json");
        Action act = () => CausalLmBundle.FromDirectory(bundle.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("genai_config.json");
    }

    /// <summary>Files that hold no generation configuration are refused.</summary>
    [Fact]
    public void FromFiles_with_no_configuration_refuses()
    {
        //Arrange
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["model.onnx"] = Path.Combine(CausalLmFixtures.TinyBundleDirectory, "model.onnx"),
        };

        Action act = () => CausalLmBundle.FromFiles(files);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("genai_config.json");
    }

    /// <summary>A configuration that is not JSON is refused as that rather than as something else.</summary>
    [Fact]
    public void FromDirectory_with_a_configuration_that_is_not_json_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.WriteConfiguration("{ this is not json");
        Action act = () => CausalLmBundle.FromDirectory(bundle.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("not valid JSON");
    }

    /// <summary>A shape this driver does not run is refused by the name the file gives it.</summary>
    /// <param name="block">The block the configuration carries.</param>
    [Theory]
    [InlineData("encoder")]
    [InlineData("encoder_decoder_init")]
    [InlineData("vision")]
    [InlineData("speech")]
    [InlineData("audio")]
    [InlineData("embedding")]
    public void FromDirectory_with_a_block_this_driver_does_not_run_refuses(string block)
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.WriteConfiguration(
            bundle.ReadConfiguration().Replace(
                "\"decoder\": {", "\"" + block + "\": { \"filename\": \"other.onnx\" }, \"decoder\": {",
                StringComparison.Ordinal));

        Action act = () => CausalLmBundle.FromDirectory(bundle.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("'" + block + "'");
    }

    /// <summary>A decoder made of several graphs run in turn is refused by name.</summary>
    [Fact]
    public void FromDirectory_with_a_pipeline_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.WriteConfiguration(
            bundle.ReadConfiguration().Replace(
                "\"filename\": \"model.onnx\"",
                "\"pipeline\": [ { \"filename\": \"model.onnx\" } ], \"filename\": \"model.onnx\"",
                StringComparison.Ordinal));

        Action act = () => CausalLmBundle.FromDirectory(bundle.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("PIPELINE");
    }

    /// <summary>A configuration with no model block is refused.</summary>
    [Fact]
    public void FromDirectory_with_no_model_block_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.WriteConfiguration("{ \"search\": { } }");
        Action act = () => CausalLmBundle.FromDirectory(bundle.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("no model block");
    }

    /// <summary>A model block with no decoder is refused.</summary>
    [Fact]
    public void FromDirectory_with_no_decoder_block_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.WriteConfiguration("{ \"model\": { \"type\": \"x\", \"vocab_size\": 4, \"context_length\": 8 } }");
        Action act = () => CausalLmBundle.FromDirectory(bundle.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("no decoder block");
    }

    /// <summary>A cache pattern with no place for the layer number in it is refused.</summary>
    [Fact]
    public void FromDirectory_with_a_cache_pattern_that_has_no_layer_number_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.WriteConfiguration(
            bundle.ReadConfiguration().Replace(
                "past_key_values.%d.key", "past_key_values.key", StringComparison.Ordinal));

        Action act = () => CausalLmBundle.FromDirectory(bundle.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("'%d'");
    }

    /// <summary>More key and value heads than attention heads is refused as the contradiction it is.</summary>
    [Fact]
    public void FromDirectory_with_more_key_value_heads_than_heads_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Replace("num_key_value_heads", "2", "8");
        Action act = () => CausalLmBundle.FromDirectory(bundle.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>()
            .Which.Message.Should().Contain("more key and value heads");
    }

    /// <summary>A decoder with no layers at all is refused rather than loaded.</summary>
    [Fact]
    public void FromDirectory_with_no_layers_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Replace("num_hidden_layers", "2", "0");
        Action act = () => CausalLmBundle.FromDirectory(bundle.DirectoryPath);

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("cache cannot be built");
    }

    /// <summary>A configuration that names several end-of-sequence tokens keeps all of them.</summary>
    [Fact]
    public void FromDirectory_reads_several_end_of_sequence_tokens()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Replace("eos_token_id", "3", "[3, 7]");

        //Act
        CausalLmBundle read = CausalLmBundle.FromDirectory(bundle.DirectoryPath);

        //Assert
        read.EndOfSequence.Should().Equal(3, 7);
    }

    /// <summary>A directory carrying a generation configuration is one this driver generates for.</summary>
    [Fact]
    public void IsCausalLmDirectory_answers_from_the_configuration()
    {
        CausalLmBundle.IsCausalLmDirectory(CausalLmFixtures.TinyBundleDirectory).Should().BeTrue();
        CausalLmBundle.IsCausalLmDirectory(AppContext.BaseDirectory).Should().BeFalse();
    }
}

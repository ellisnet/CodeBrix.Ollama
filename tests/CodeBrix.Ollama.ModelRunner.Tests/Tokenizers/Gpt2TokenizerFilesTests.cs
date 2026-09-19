using System;
using System.Collections.Generic;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Building a tokenizer out of a bundle's files: what is read, and what is refused by the name of the thing
/// that was found instead.
/// </summary>
public sealed class Gpt2TokenizerFilesTests
{
    /// <summary>The tiny bundle's files build a tokenizer holding the vocabulary and the merges.</summary>
    [Fact]
    public void Load_reads_the_vocabulary_and_the_merges()
    {
        //Act
        Gpt2ByteLevelTokenizer tokenizer = Gpt2TokenizerFiles.Load(CausalLmFixtures.TinyBundleFiles());

        //Assert
        tokenizer.VocabularySize.Should().Be(320);
        tokenizer.MergeCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The `#version` header a builder's export carries is not a merge, and dropping it is what makes the
    /// table this reads the same table the published Python tokenizer holds.
    /// </summary>
    [Fact]
    public void Load_drops_a_version_header_and_keeps_every_merge_after_it()
    {
        //Arrange
        string[] lines = File.ReadAllLines(
            Path.Combine(CausalLmFixtures.TinyBundleDirectory, "merges.txt"));

        //Act
        Gpt2ByteLevelTokenizer tokenizer = Gpt2TokenizerFiles.Load(CausalLmFixtures.TinyBundleFiles());

        //Assert
        lines[0].Should().StartWith("#");
        tokenizer.MergeCount.Should().Be(lines.Length - 1);
    }

    /// <summary>
    /// A merge file with no header keeps every line, because the rule drops a first line only when it begins
    /// with a hash. The bundles this driver reads always carry the header, so the two readings of such a file
    /// - this one and the published tokenizer's, which drops the first line whatever it is - never differ in
    /// practice; this is the case where they would.
    /// </summary>
    [Fact]
    public void Load_keeps_the_first_line_of_a_merge_file_that_has_no_header()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        string[] lines = File.ReadAllLines(
            Path.Combine(CausalLmFixtures.TinyBundleDirectory, "merges.txt"));
        bundle.Add("merges.txt", string.Join("\n", lines, 1, lines.Length - 1) + "\n");

        //Act
        Gpt2ByteLevelTokenizer tokenizer = Gpt2TokenizerFiles.Load(Files(bundle));

        //Assert
        tokenizer.MergeCount.Should().Be(lines.Length - 1);
    }

    /// <summary>A bundle whose tokenizer is a SentencePiece model is refused by that name.</summary>
    [Fact]
    public void Load_with_a_sentencepiece_model_refuses_by_name()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Remove("vocab.json");
        bundle.Add("tokenizer.model", "not really one");
        Action act = () => Gpt2TokenizerFiles.Load(Files(bundle));

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("SentencePiece");
    }

    /// <summary>A bundle carrying only a tokenizer.json is refused by that name.</summary>
    [Fact]
    public void Load_with_only_a_tokenizer_json_refuses_by_name()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Remove("vocab.json");
        bundle.Add("tokenizer.json", "{}");
        Action act = () => Gpt2TokenizerFiles.Load(Files(bundle));

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("tokenizer.json");
    }

    /// <summary>A bundle with no tokenizer at all is refused by the file it is missing.</summary>
    [Fact]
    public void Load_with_no_tokenizer_refuses_by_the_missing_file()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Remove("merges.txt");
        Action act = () => Gpt2TokenizerFiles.Load(Files(bundle));

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("merges.txt");
    }

    /// <summary>A vocabulary that is not JSON is refused as that.</summary>
    [Fact]
    public void Load_with_a_vocabulary_that_is_not_json_refuses()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Add("vocab.json", "{ not json");
        Action act = () => Gpt2TokenizerFiles.Load(Files(bundle));

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("not valid JSON");
    }

    /// <summary>An added token asking for a matching rule this library does not implement is refused by name.</summary>
    /// <param name="rule">The rule the bundle asks for.</param>
    [Theory]
    [InlineData("lstrip")]
    [InlineData("rstrip")]
    [InlineData("single_word")]
    public void Load_with_an_added_token_rule_this_library_does_not_implement_refuses(string rule)
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        string configuration = File.ReadAllText(
            Path.Combine(bundle.DirectoryPath, "tokenizer_config.json"));
        bundle.Add(
            "tokenizer_config.json",
            configuration.Replace(
                "\"" + rule + "\": false", "\"" + rule + "\": true", StringComparison.Ordinal));

        Action act = () => Gpt2TokenizerFiles.Load(Files(bundle));

        //Act and assert
        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain(rule);
    }

    /// <summary>A bundle with no tokenizer configuration at all still builds a tokenizer from the pair.</summary>
    [Fact]
    public void Load_without_a_tokenizer_configuration_still_reads_the_pair()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Remove("tokenizer_config.json");
        bundle.Remove("special_tokens_map.json");

        //Act
        Gpt2ByteLevelTokenizer tokenizer = Gpt2TokenizerFiles.Load(Files(bundle));

        //Assert
        tokenizer.VocabularySize.Should().Be(320);
        tokenizer.Encode("<eos>", true, true).Should().HaveCountGreaterThan(1);
    }

    /// <summary>An added_tokens.json is read as well, and its tokens are matched as text.</summary>
    [Fact]
    public void Load_reads_an_added_tokens_file()
    {
        //Arrange
        using CausalLmScratchBundle bundle = new CausalLmScratchBundle();
        bundle.Add("added_tokens.json", "{ \"<eos>\": 3 }");

        //Act
        Gpt2ByteLevelTokenizer tokenizer = Gpt2TokenizerFiles.Load(Files(bundle));

        //Assert
        tokenizer.Encode("<eos>", true, true).Should().Equal(3);
    }

    private static IReadOnlyDictionary<string, string> Files(CausalLmScratchBundle bundle)
    {
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(bundle.DirectoryPath))
        {
            files[Path.GetFileName(path)] = path;
        }

        return files;
    }
}

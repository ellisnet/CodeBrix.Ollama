using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The managed byte-level byte-pair encoder against the PUBLISHED Python tokenizer, case by case over a
/// corpus that was tokenized once by the Python and checked in.
/// </summary>
/// <remarks>
/// This is the test that matters: a tokenizer that is nearly right produces a model that runs and writes
/// nonsense, so what is asserted is the token NUMBERS, not the text they came from.
/// </remarks>
public sealed class Gpt2ByteLevelTokenizerTests
{
    private static readonly Lazy<Gpt2ByteLevelTokenizer> Tokenizer =
        new Lazy<Gpt2ByteLevelTokenizer>(() => Gpt2TokenizerFiles.Load(CausalLmFixtures.TinyBundleFiles()));

    /// <summary>Every case's token numbers are the Python's, exactly.</summary>
    /// <param name="index">Which case.</param>
    [Theory]
    [MemberData(nameof(CausalLmFixtures.AllTokenizerCases), MemberType = typeof(CausalLmFixtures))]
    public void Encode_gives_the_published_token_numbers(int index)
    {
        //Arrange
        CausalLmTokenizerCase one = CausalLmFixtures.TokenizerCases.Cases[index];

        //Act
        IReadOnlyList<int> ids = Tokenizer.Value.Encode(one.Text, true, true);

        //Assert
        ids.Should().Equal(one.Ids);
    }

    /// <summary>Every case's token numbers decode back to what the Python decodes them to.</summary>
    /// <param name="index">Which case.</param>
    [Theory]
    [MemberData(nameof(CausalLmFixtures.AllTokenizerCases), MemberType = typeof(CausalLmFixtures))]
    public void Decode_gives_the_published_text(int index)
    {
        //Arrange
        CausalLmTokenizerCase one = CausalLmFixtures.TokenizerCases.Cases[index];

        //Act
        string text = Tokenizer.Value.Decode(one.Ids, true);

        //Assert
        text.Should().Be(one.DecodedWithSpecials);
    }

    /// <summary>Dropping the special tokens on the way back out is the Python's answer too.</summary>
    /// <param name="index">Which case.</param>
    [Theory]
    [MemberData(nameof(CausalLmFixtures.AllTokenizerCases), MemberType = typeof(CausalLmFixtures))]
    public void Decode_without_special_tokens_gives_the_published_text(int index)
    {
        //Arrange
        CausalLmTokenizerCase one = CausalLmFixtures.TokenizerCases.Cases[index];

        //Act
        string text = Tokenizer.Value.Decode(one.Ids, false);

        //Assert
        text.Should().Be(one.DecodedWithoutSpecials);
    }

    /// <summary>The symbol strings the numbers stand for are the Python's pieces.</summary>
    /// <param name="index">Which case.</param>
    [Theory]
    [MemberData(nameof(CausalLmFixtures.AllTokenizerCases), MemberType = typeof(CausalLmFixtures))]
    public void Symbol_gives_the_published_pieces(int index)
    {
        //Arrange
        CausalLmTokenizerCase one = CausalLmFixtures.TokenizerCases.Cases[index];
        List<string> pieces = new List<string>();

        //Act
        foreach (int id in one.Ids) pieces.Add(Tokenizer.Value.Symbol(id));

        //Assert
        pieces.Should().Equal(one.Pieces);
    }

    /// <summary>Text that carries no special token round trips whatever is in it.</summary>
    /// <param name="index">Which case.</param>
    [Theory]
    [MemberData(nameof(CausalLmFixtures.AllTokenizerCases), MemberType = typeof(CausalLmFixtures))]
    public void Encode_and_decode_round_trip(int index)
    {
        //Arrange
        CausalLmTokenizerCase one = CausalLmFixtures.TokenizerCases.Cases[index];

        //Act
        string back = Tokenizer.Value.Decode(Tokenizer.Value.Encode(one.Text, true, true), true);

        //Assert
        back.Should().Be(one.Text);
    }

    /// <summary>
    /// The bytes one token contributes are the bytes its part of the text is made of, so a caller assembling
    /// UTF-8 as it goes gets the same text as one decoding the whole run at the end.
    /// </summary>
    /// <param name="index">Which case.</param>
    [Theory]
    [MemberData(nameof(CausalLmFixtures.AllTokenizerCases), MemberType = typeof(CausalLmFixtures))]
    public void Bytes_assembled_token_by_token_give_the_same_text(int index)
    {
        //Arrange
        CausalLmTokenizerCase one = CausalLmFixtures.TokenizerCases.Cases[index];
        Utf8Assembler assembler = new Utf8Assembler();
        System.Text.StringBuilder text = new System.Text.StringBuilder();

        //Act
        foreach (int id in one.Ids) text.Append(assembler.Append(Tokenizer.Value.Bytes(id, true)));
        text.Append(assembler.Flush());

        //Assert
        text.ToString().Should().Be(one.DecodedWithSpecials);
    }

    /// <summary>Not parsing special tokens encodes their text as ordinary characters instead.</summary>
    [Fact]
    public void Encode_without_parsing_special_tokens_writes_them_as_text()
    {
        //Arrange
        IReadOnlyList<int> parsed = Tokenizer.Value.Encode("<bos>", true, true);

        //Act
        IReadOnlyList<int> literal = Tokenizer.Value.Encode("<bos>", true, false);

        //Assert
        parsed.Should().HaveCount(1);
        literal.Should().HaveCountGreaterThan(1);
        Tokenizer.Value.Decode(literal, true).Should().Be("<bos>");
    }

    /// <summary>A special token is one; an ordinary one is not.</summary>
    [Fact]
    public void IsSpecial_names_only_the_bundles_own_special_tokens()
    {
        //Arrange
        IReadOnlyList<int> special = Tokenizer.Value.Encode("<eos>", true, true);
        IReadOnlyList<int> ordinary = Tokenizer.Value.Encode("a", true, true);

        //Act and assert
        Tokenizer.Value.IsSpecial(special[0]).Should().BeTrue();
        Tokenizer.Value.IsSpecial(ordinary[0]).Should().BeFalse();
    }

    /// <summary>This bundle says its tokenizer adds nothing in front, so asking for special tokens adds nothing.</summary>
    [Fact]
    public void Encode_with_special_tokens_adds_nothing_when_the_bundle_says_so() =>
        Tokenizer.Value.Encode("hello", true, true)
            .Should().Equal(Tokenizer.Value.Encode("hello", false, true));

    /// <summary>A bundle that DOES ask for a beginning-of-sequence token gets one, and only when asked.</summary>
    [Fact]
    public void Encode_adds_the_beginning_of_sequence_token_when_the_bundle_asks_for_it()
    {
        //Arrange
        Gpt2ByteLevelTokenizer tokenizer = Build(addBeginningOfSequence: true, addPrefixSpace: false);

        //Act
        IReadOnlyList<int> with = tokenizer.Encode("b", true, true);
        IReadOnlyList<int> without = tokenizer.Encode("b", false, true);

        //Assert
        with.Should().Equal(2, 6);
        without.Should().Equal(6);
    }

    /// <summary>A bundle that asks for a space in front of the text gets one.</summary>
    [Fact]
    public void Encode_puts_a_space_in_front_when_the_bundle_asks_for_it()
    {
        //Arrange
        Gpt2ByteLevelTokenizer tokenizer = Build(addBeginningOfSequence: false, addPrefixSpace: true);

        //Act
        IReadOnlyList<int> ids = tokenizer.Encode("b", true, true);

        //Assert
        tokenizer.Decode(ids, true).Should().Be(" b");
    }

    /// <summary>A merge the table does not hold leaves the two symbols apart.</summary>
    [Fact]
    public void Encode_leaves_symbols_the_table_cannot_join_apart()
    {
        //Arrange
        Gpt2ByteLevelTokenizer tokenizer = Build(addBeginningOfSequence: false, addPrefixSpace: false);

        //Act
        IReadOnlyList<int> ids = tokenizer.Encode("ab", true, true);

        //Assert
        ids.Should().HaveCount(2);
    }

    /// <summary>A merge the table does hold joins them, and the joined symbol is one token.</summary>
    [Fact]
    public void Encode_joins_symbols_the_table_holds()
    {
        //Arrange
        Gpt2ByteLevelTokenizer tokenizer = Build(
            addBeginningOfSequence: false, addPrefixSpace: false, merges: new[] { "a b" });

        //Act
        IReadOnlyList<int> ids = tokenizer.Encode("ab", true, true);

        //Assert
        ids.Should().Equal(7);
    }

    /// <summary>A vocabulary with nothing in it is refused rather than loaded.</summary>
    [Fact]
    public void Constructor_with_an_empty_vocabulary_refuses()
    {
        //Arrange
        Action act = () => new Gpt2ByteLevelTokenizer(
            new Dictionary<string, int>(), new List<string>(), Gpt2TokenizerSettings.Default());

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("vocab.json");
    }

    /// <summary>A token number below nought is refused rather than loaded.</summary>
    [Fact]
    public void Constructor_with_a_negative_token_number_refuses()
    {
        //Arrange
        Dictionary<string, int> vocabulary = new Dictionary<string, int> { ["a"] = -1 };
        Action act = () => new Gpt2ByteLevelTokenizer(
            vocabulary, new List<string>(), Gpt2TokenizerSettings.Default());

        //Act and assert
        act.Should().Throw<ModelLoadException>().Which.Message.Should().Contain("below nought");
    }

    private static Gpt2ByteLevelTokenizer Build(
        bool addBeginningOfSequence, bool addPrefixSpace, IReadOnlyList<string> merges = null)
    {
        //A vocabulary small enough to reason about: four specials, then the symbols of three bytes, then the
        //one merged symbol.
        Dictionary<string, int> vocabulary = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["<pad>"] = 0,
            ["<unk>"] = 1,
            ["<bos>"] = 2,
            ["<eos>"] = 3,
            ["Ġ"] = 4,
            ["a"] = 5,
            ["b"] = 6,
            ["ab"] = 7,
            ["Ġb"] = 8,
        };

        List<Gpt2AddedToken> added = new List<Gpt2AddedToken>
        {
            new Gpt2AddedToken("<pad>", 0, true),
            new Gpt2AddedToken("<unk>", 1, true),
            new Gpt2AddedToken("<bos>", 2, true),
            new Gpt2AddedToken("<eos>", 3, true),
        };

        return new Gpt2ByteLevelTokenizer(
            vocabulary,
            merges ?? Array.Empty<string>(),
            new Gpt2TokenizerSettings(
                addBeginningOfSequence, addPrefixSpace, "<bos>", "<unk>", added));
    }
}

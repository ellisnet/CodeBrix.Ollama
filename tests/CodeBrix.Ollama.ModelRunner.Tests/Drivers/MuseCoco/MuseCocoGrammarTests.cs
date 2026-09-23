using System;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

public sealed class MuseCocoGrammarTests
{
    private static readonly string[] Vocabulary =
    {
        "<s>", "<pad>", "</s>", "<unk>", "<sep>", "I1s2_0_0", "Q2", "None",
        "s-9", "s-8", "t-32", "o-0", "o-6", "i-0", "i-128", "p-60", "p-64", "p-164", "d-6", "v-20", "b-1"
    };

    [Theory]
    [InlineData(1, 1)]
    [InlineData(15, 1)]
    [InlineData(15, 0.5)]
    public void Sample_filters_invalid_continuations_before_top_k_and_top_p_when_eos_is_blocked(int topK, double topP)
    {
        //Arrange: U1's first bad transition was o-6 -> s-9 with EOS strongly preferred.
        var grammar = new Remigen2Grammar(Vocabulary);
        Feed(grammar, "s-9 t-32 o-0 i-0 p-60 d-6 v-20 o-6");
        float[] logits = Enumerable.Repeat(-1000F, Vocabulary.Length).ToArray();
        logits[Id("</s>")] = 25;
        logits[Id("s-9")] = 14;
        logits[Id("s-8")] = 13;
        logits[Id("d-6")] = 12;
        logits[Id("v-20")] = 11;
        logits[Id("<sep>")] = 10;
        logits[Id("i-0")] = 8;
        var options = new MuseCocoGenerationOptions { TopK = topK, TopP = topP };

        //Act
        int token = MuseCocoSampler.Sample(logits, Id("</s>"), false, grammar, options, new GenerationRandom(20260921));

        //Assert
        Vocabulary[token].Should().Be("i-0");
        logits[Id("s-9")].Should().Be(14, "sampling must not rewrite the model's output buffer");
    }

    [Theory]
    [InlineData("", "d-6")]
    [InlineData("s-9", "d-6")]
    [InlineData("o-0", "d-6")]
    [InlineData("o-0 i-0", "v-20")]
    [InlineData("o-0 p-60", "v-20")]
    [InlineData("o-0 p-60", "b-1")]
    [InlineData("o-0 p-60 d-6", "d-6")]
    [InlineData("o-0 p-60 d-6", "p-64")]
    [InlineData("o-0 p-60 d-6 v-20", "s-8")]
    [InlineData("o-0 p-60 d-6 v-20 b-1", "p-64")]
    [InlineData("Q2", "None")]
    public void Allows_rejects_invalid_token_order(string prefix, string candidate)
    {
        //Arrange
        var grammar = new Remigen2Grammar(Vocabulary);
        Feed(grammar, prefix);

        //Act
        bool allowed = grammar.Allows(Id(candidate));

        //Assert
        allowed.Should().BeFalse();
    }

    [Theory]
    [InlineData("<s>")]
    [InlineData("<pad>")]
    [InlineData("<unk>")]
    [InlineData("<sep>")]
    [InlineData("I1s2_0_0")]
    [InlineData("b-2")]
    [InlineData("s-254")]
    [InlineData("t-49")]
    [InlineData("o--1")]
    [InlineData("i-129")]
    [InlineData("p-256")]
    [InlineData("d--1")]
    [InlineData("v-32")]
    public void Allows_excludes_non_music_tokens_and_invalid_values(string candidate)
    {
        //Arrange
        string[] vocabulary = Vocabulary.Contains(candidate) ? Vocabulary : Vocabulary.Append(candidate).ToArray();
        int id = Array.IndexOf(vocabulary, candidate);
        var grammar = new Remigen2Grammar(vocabulary);
        string[] sequence = "Q2 s-9 t-32 o-0 i-0 p-60 d-6 v-20 b-1".Split(' ');

        //Act and assert
        grammar.Allows(id).Should().BeFalse();
        foreach (string token in sequence)
        {
            grammar.Accept(Array.IndexOf(vocabulary, token));
            grammar.Allows(id).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("Q2 s-9 t-32 o-0 i-0 p-60 d-6 v-20 p-64 d-6 v-20 b-1 s-8 t-32 o-0 i-128 p-164 d-6 v-20 b-1 </s>")]
    [InlineData("o-0 t-32 p-60 d-6 v-20 o-6 p-64 d-6 v-20 b-1 b-1 </s>")]
    [InlineData("None b-1 s-9 o-0 t-32 i-0 p-60 </s>")]
    [InlineData("s-9 o-0 i-0 p-60 d-6 </s>")]
    public void Accept_preserves_chords_percussion_implicit_defaults_and_incomplete_endings(string text)
    {
        //Arrange
        var grammar = new Remigen2Grammar(Vocabulary);

        //Act
        Action accept = () => Feed(grammar, text);

        //Assert
        accept.Should().NotThrow();
        Remigen2Decoder.Decode(text.Split(' ').Select(Id).ToArray(), Vocabulary).Should().NotBeNull();
    }

    [Fact]
    public void Sample_allows_eos_once_the_minimum_is_satisfied_and_refuses_an_empty_candidate_set()
    {
        //Arrange
        var grammar = new Remigen2Grammar(new[] { "</s>", "<pad>" });
        float[] logits = { 10, 20 };
        var options = new MuseCocoGenerationOptions { TopK = 1 };

        //Act
        int ended = MuseCocoSampler.Sample(logits, 0, true, grammar, options, new GenerationRandom(0));
        Action noCandidate = () => MuseCocoSampler.Sample(logits, 0, false, grammar, options, new GenerationRandom(0));

        //Assert
        ended.Should().Be(0);
        noCandidate.Should().Throw<InferenceException>().WithMessage("*No music token*");
    }

    private static int Id(string word) => Array.IndexOf(Vocabulary, word);

    private static void Feed(Remigen2Grammar grammar, string text)
    {
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries)) grammar.Accept(Id(word));
    }
}

using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// How one token is chosen from a row of scores: the penalties, the truncations, the temperature and the
/// draw, in the order the native half of this library applies them.
/// </summary>
public sealed class CausalLmSamplerTests
{
    /// <summary>A temperature of nought takes the largest score, every time.</summary>
    [Fact]
    public void Choose_at_temperature_nought_takes_the_largest_score()
    {
        //Arrange
        CausalLmSampler sampler = Sampler(new SamplingOptions { Temperature = 0f, RepeatLastN = 0 });

        //Act
        int token = sampler.Choose(new[] { 1f, 9f, 3f, 5f });

        //Assert
        token.Should().Be(1);
    }

    /// <summary>Two scores the model likes equally go to the lower token, every time.</summary>
    [Fact]
    public void Choose_at_temperature_nought_breaks_a_tie_towards_the_lower_token()
    {
        //Act and assert
        for (int seed = 0; seed < 5; seed++)
        {
            CausalLmSampler sampler = Sampler(
                new SamplingOptions { Temperature = 0f, RepeatLastN = 0 }, (ulong)seed);
            sampler.Choose(new[] { 4f, 4f, 1f }).Should().Be(0);
        }
    }

    /// <summary>Greedy is greedy whatever the seed is, so a seeded run and an unseeded one agree.</summary>
    [Fact]
    public void IsGreedy_is_true_only_at_temperature_nought_or_below()
    {
        //Act and assert
        Sampler(new SamplingOptions { Temperature = 0f }).IsGreedy.Should().BeTrue();
        Sampler(new SamplingOptions { Temperature = -1f }).IsGreedy.Should().BeTrue();
        Sampler(new SamplingOptions { Temperature = 0.8f }).IsGreedy.Should().BeFalse();
    }

    /// <summary>The same seed and the same scores give the same tokens, run after run.</summary>
    [Fact]
    public void Choose_with_the_same_seed_gives_the_same_tokens()
    {
        //Arrange
        List<int> first = Draw(1234);

        //Act
        List<int> second = Draw(1234);

        //Assert
        first.Should().Equal(second);
    }

    /// <summary>Another seed gives another stream, which is what a seed is for.</summary>
    [Fact]
    public void Choose_with_another_seed_gives_other_tokens() =>
        Draw(1234).Should().NotEqual(Draw(9876));

    /// <summary>Keeping one token is greedy however warm the temperature is.</summary>
    [Fact]
    public void Choose_with_top_k_of_one_takes_the_largest_score()
    {
        //Arrange
        CausalLmSampler sampler = Sampler(
            new SamplingOptions { Temperature = 5f, TopK = 1, TopP = 1f, RepeatLastN = 0 });

        //Act and assert
        for (int i = 0; i < 10; i++) sampler.Choose(new[] { 1f, 9f, 3f, 5f }).Should().Be(1);
    }

    /// <summary>Top-k keeps the k largest and nothing else can be drawn.</summary>
    [Fact]
    public void Choose_with_top_k_draws_only_from_the_k_largest()
    {
        //Arrange
        CausalLmSampler sampler = Sampler(
            new SamplingOptions { Temperature = 10f, TopK = 2, TopP = 1f, RepeatLastN = 0 });
        HashSet<int> seen = new HashSet<int>();

        //Act
        for (int i = 0; i < 200; i++) seen.Add(sampler.Choose(new[] { 1f, 9f, 3f, 5f }));

        //Assert
        seen.Should().BeEquivalentTo(new[] { 1, 3 });
    }

    /// <summary>Nucleus sampling keeps the smallest set that reaches the share asked for.</summary>
    [Fact]
    public void Choose_with_top_p_draws_only_from_the_most_likely_share()
    {
        //Arrange - one token holds almost all the probability, so a small share keeps it alone.
        CausalLmSampler sampler = Sampler(
            new SamplingOptions { Temperature = 1f, TopK = 0, TopP = 0.5f, RepeatLastN = 0 });
        HashSet<int> seen = new HashSet<int>();

        //Act
        for (int i = 0; i < 200; i++) seen.Add(sampler.Choose(new[] { 0f, 10f, 0f, 0f }));

        //Assert
        seen.Should().BeEquivalentTo(new[] { 1 });
    }

    /// <summary>The smallest-share rule drops what is far less likely than the best.</summary>
    [Fact]
    public void Choose_with_min_p_drops_what_is_far_less_likely_than_the_best()
    {
        //Arrange
        CausalLmSampler sampler = Sampler(
            new SamplingOptions { Temperature = 1f, TopK = 0, TopP = 1f, MinP = 0.5f, RepeatLastN = 0 });
        HashSet<int> seen = new HashSet<int>();

        //Act
        for (int i = 0; i < 200; i++) seen.Add(sampler.Choose(new[] { 0f, 4f, 3.8f, 0f }));

        //Assert
        seen.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    /// <summary>Locally typical sampling keeps what is closest to the average surprise.</summary>
    [Fact]
    public void Choose_with_typical_p_keeps_a_smaller_set()
    {
        //Arrange
        CausalLmSampler sampler = Sampler(
            new SamplingOptions
            {
                Temperature = 1f, TopK = 0, TopP = 1f, TypicalP = 0.3f, RepeatLastN = 0,
            });
        HashSet<int> seen = new HashSet<int>();

        //Act
        for (int i = 0; i < 200; i++) seen.Add(sampler.Choose(new[] { 0f, 4f, 3.8f, 0f }));

        //Assert
        seen.Should().HaveCountLessThan(4);
    }

    /// <summary>
    /// A repeat penalty pushes a token the request has already produced towards nought, which is enough to
    /// change a greedy choice.
    /// </summary>
    [Fact]
    public void Choose_after_a_repeat_penalty_avoids_what_was_already_said()
    {
        //Arrange
        CausalLmSampler sampler = Sampler(
            new SamplingOptions { Temperature = 0f, RepeatPenalty = 4f, RepeatLastN = 64 });
        float[] logits = { 1f, 9f, 8f, 5f };

        //Act
        int first = sampler.Choose((float[])logits.Clone());
        sampler.Accept(first);
        int second = sampler.Choose((float[])logits.Clone());

        //Assert
        first.Should().Be(1);
        second.Should().Be(2);
    }

    /// <summary>A penalty window of nought turns the penalties off however large they are.</summary>
    [Fact]
    public void Choose_with_no_penalty_window_repeats_freely()
    {
        //Arrange
        CausalLmSampler sampler = Sampler(
            new SamplingOptions { Temperature = 0f, RepeatPenalty = 4f, RepeatLastN = 0 });
        float[] logits = { 1f, 9f, 8f, 5f };

        //Act
        sampler.Accept(sampler.Choose((float[])logits.Clone()));

        //Assert
        sampler.Choose((float[])logits.Clone()).Should().Be(1);
    }

    /// <summary>A token that falls out of the penalty window stops being penalised.</summary>
    [Fact]
    public void Accept_forgets_a_token_that_fell_out_of_the_window()
    {
        //Arrange
        CausalLmSampler sampler = Sampler(
            new SamplingOptions { Temperature = 0f, RepeatPenalty = 4f, RepeatLastN = 2 });
        float[] logits = { 1f, 9f, 8f, 5f };

        //Act
        sampler.Accept(1);
        sampler.Accept(0);
        sampler.Accept(3);

        //Assert - token 1 is three tokens back, so a window of two no longer sees it.
        sampler.Choose((float[])logits.Clone()).Should().Be(1);
    }

    /// <summary>A presence penalty on its own is enough to change a greedy choice.</summary>
    [Fact]
    public void Choose_after_a_presence_penalty_avoids_what_was_already_said()
    {
        //Arrange
        CausalLmSampler sampler = Sampler(
            new SamplingOptions
            {
                Temperature = 0f, RepeatPenalty = 1f, PresencePenalty = 2f, RepeatLastN = 64,
            });
        float[] logits = { 1f, 9f, 8f, 5f };

        //Act
        sampler.Accept(1);

        //Assert
        sampler.Choose((float[])logits.Clone()).Should().Be(2);
    }

    /// <summary>A frequency penalty grows with how often a token was said.</summary>
    [Fact]
    public void Choose_after_a_frequency_penalty_grows_with_the_count()
    {
        //Arrange
        CausalLmSampler sampler = Sampler(
            new SamplingOptions
            {
                Temperature = 0f, RepeatPenalty = 1f, FrequencyPenalty = 0.6f, RepeatLastN = 64,
            });
        float[] logits = { 1f, 9f, 8f, 5f };

        //Act
        sampler.Accept(1);
        int afterOne = sampler.Choose((float[])logits.Clone());
        sampler.Accept(1);
        int afterTwo = sampler.Choose((float[])logits.Clone());

        //Assert
        afterOne.Should().Be(1);
        afterTwo.Should().Be(2);
    }

    /// <summary>The largest score's token is the lowest one when several share it.</summary>
    [Fact]
    public void ArgMax_breaks_a_tie_towards_the_lower_token() =>
        CausalLmSampler.ArgMax(new[] { 2f, 7f, 7f, 1f }).Should().Be(1);

    /// <summary>A row that is nothing but missing numbers is refused rather than answered.</summary>
    [Fact]
    public void ArgMax_with_no_usable_score_refuses()
    {
        //Arrange
        Action act = () => CausalLmSampler.ArgMax(new[] { float.NaN, float.NaN });

        //Act and assert
        act.Should().Throw<InferenceException>().Which.Message.Should().Contain("no usable score");
    }

    private static CausalLmSampler Sampler(SamplingOptions options, ulong seed = 20260918) =>
        new CausalLmSampler(options, seed);

    private static List<int> Draw(ulong seed)
    {
        CausalLmSampler sampler = Sampler(
            new SamplingOptions { Temperature = 1.5f, TopK = 0, TopP = 1f, RepeatLastN = 0 }, seed);

        List<int> tokens = new List<int>();
        for (int i = 0; i < 40; i++) tokens.Add(sampler.Choose(new[] { 1f, 2f, 3f, 2.5f, 1.5f }));
        return tokens;
    }
}

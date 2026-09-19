using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// How one token is chosen: the publisher's temperature, nucleus and top-k sampling, with the tokens that are
/// not allowed at this position taken out.
/// </summary>
public sealed class SkyTntSamplerTests
{
    /// <summary>Keeping one token makes the choice the most likely ALLOWED one.</summary>
    [Fact]
    public void Sample_with_one_token_kept_takes_the_most_likely_allowed_one()
    {
        //Arrange
        float[] logits = { 1f, 9f, 3f, 5f, 2f };
        bool[] allowed = { true, false, true, true, false };

        //Act
        int token = SkyTntSampler.Sample(logits, allowed, 1.0, 1.0, 1, new SkyTntRandom(1));

        //Assert
        token.Should().Be(3);
    }

    /// <summary>Two tokens the model likes equally go to the lower one, every time.</summary>
    [Fact]
    public void Sample_with_one_token_kept_breaks_a_tie_the_same_way_every_time()
    {
        //Arrange
        float[] logits = { 4f, 4f, 1f };
        bool[] allowed = { true, true, true };

        //Act and assert
        for (int i = 0; i < 5; i++)
        {
            SkyTntSampler.Sample(logits, allowed, 1.0, 1.0, 1, new SkyTntRandom(i)).Should().Be(0);
        }
    }

    /// <summary>Keeping one token draws no random number at all, so the stream is untouched.</summary>
    [Fact]
    public void Sample_with_one_token_kept_draws_no_random_number()
    {
        //Arrange
        float[] logits = { 1f, 7f, 3f };
        bool[] allowed = { true, true, true };
        SkyTntRandom random = new SkyTntRandom(99);
        double first = random.NextDouble();

        SkyTntRandom other = new SkyTntRandom(99);
        SkyTntSampler.Sample(logits, allowed, 1.0, 1.0, 1, other);

        //Act
        double afterSampling = other.NextDouble();

        //Assert
        afterSampling.Should().Be(first);
    }

    /// <summary>Nothing outside the mask is ever chosen, however likely the model thought it.</summary>
    [Fact]
    public void Sample_never_chooses_a_token_the_mask_refuses()
    {
        //Arrange
        float[] logits = new float[64];
        for (int i = 0; i < logits.Length; i++) logits[i] = i;
        bool[] allowed = new bool[64];
        allowed[3] = true;
        allowed[11] = true;
        SkyTntRandom random = new SkyTntRandom(7);

        //Act and assert
        for (int i = 0; i < 200; i++)
        {
            int token = SkyTntSampler.Sample(logits, allowed, 1.0, 1.0, 20, random);
            (token == 3 || token == 11).Should().BeTrue();
        }
    }

    /// <summary>Keeping a few tokens never reaches past them.</summary>
    [Fact]
    public void Sample_keeps_at_most_the_number_of_tokens_it_was_given()
    {
        //Arrange
        float[] logits = { 10f, 9f, 8f, 7f, 6f, 5f };
        bool[] allowed = { true, true, true, true, true, true };
        SkyTntRandom random = new SkyTntRandom(4242);
        HashSet<int> chosen = new HashSet<int>();

        //Act
        for (int i = 0; i < 500; i++)
        {
            chosen.Add(SkyTntSampler.Sample(logits, allowed, 1.0, 1.0, 3, random));
        }

        //Assert
        chosen.Should().HaveCount(3);
        chosen.Should().Contain(0);
        chosen.Should().Contain(1);
        chosen.Should().Contain(2);
    }

    /// <summary>A nucleus threshold keeps at least the most likely token, however sharp the answer is.</summary>
    [Fact]
    public void Sample_with_a_tiny_threshold_keeps_only_the_most_likely_token()
    {
        //Arrange
        float[] logits = { 0.1f, 8f, 0.2f, 0.3f };
        bool[] allowed = { true, true, true, true };
        SkyTntRandom random = new SkyTntRandom(11);

        //Act and assert
        for (int i = 0; i < 50; i++)
        {
            SkyTntSampler.Sample(logits, allowed, 1.0, 0.0001, 20, random).Should().Be(1);
        }
    }

    /// <summary>The same seed and the same answer give the same choices, every time.</summary>
    [Fact]
    public void Sample_with_the_same_seed_makes_the_same_choices()
    {
        //Arrange
        float[] logits = new float[32];
        for (int i = 0; i < logits.Length; i++) logits[i] = MathF.Sin(i) * 3f;
        bool[] allowed = new bool[32];
        for (int i = 0; i < allowed.Length; i++) allowed[i] = true;

        //Act
        List<int> first = Draw(logits, allowed, 20250918);
        List<int> again = Draw(logits, allowed, 20250918);
        List<int> other = Draw(logits, allowed, 20250919);

        //Assert
        again.Should().Equal(first);
        other.Should().NotEqual(first);
    }

    /// <summary>A temperature below one sharpens the answer and one above it flattens it.</summary>
    [Fact]
    public void Softmax_is_sharpened_by_a_low_temperature_and_flattened_by_a_high_one()
    {
        //Arrange
        float[] logits = { 1f, 2f, 3f };

        //Act
        float[] cold = SkyTntSampler.Softmax(logits, 0.25, 3);
        float[] plain = SkyTntSampler.Softmax(logits, 1.0, 3);
        float[] warm = SkyTntSampler.Softmax(logits, 4.0, 3);

        //Assert
        cold[2].Should().BeGreaterThan(plain[2]);
        warm[2].Should().BeLessThan(plain[2]);
        (cold[0] + cold[1] + cold[2]).Should().BeApproximately(1f, 1e-5f);
        (warm[0] + warm[1] + warm[2]).Should().BeApproximately(1f, 1e-5f);
    }

    /// <summary>The largest number decides the answer, however large the numbers are.</summary>
    [Fact]
    public void Softmax_of_very_large_numbers_does_not_overflow()
    {
        //Arrange
        float[] logits = { 400f, 401f, 399f };

        //Act
        float[] probabilities = SkyTntSampler.Softmax(logits, 1.0, 3);

        //Assert
        probabilities[1].Should().BeGreaterThan(probabilities[0]);
        (probabilities[0] + probabilities[1] + probabilities[2]).Should().BeApproximately(1f, 1e-5f);
    }

    /// <summary>A position where nothing at all is allowed is an error rather than a wrong answer.</summary>
    [Fact]
    public void Sample_with_nothing_allowed_is_refused()
    {
        //Arrange
        Action act = () => SkyTntSampler.Sample(
            new[] { 1f, 2f }, new[] { false, false }, 1.0, 1.0, 1, new SkyTntRandom(1));

        //Act and assert
        act.Should().Throw<InferenceException>();
    }

    private static List<int> Draw(float[] logits, bool[] allowed, long seed)
    {
        SkyTntRandom random = new SkyTntRandom(seed);
        List<int> drawn = new List<int>();
        for (int i = 0; i < 40; i++)
        {
            drawn.Add(SkyTntSampler.Sample(logits, allowed, 1.0, 0.95, 8, random));
        }

        return drawn;
    }
}

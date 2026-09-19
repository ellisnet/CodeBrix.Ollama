using System;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The rotary embedding, held to the plain statement of what it is: each pair of values turned through the
/// angle the caches hold.
/// </summary>
/// <remarks>
/// WHICH VALUES ARE PAIRED IS THE WHOLE OF THE DIFFERENCE between the two layouts, and getting it round the
/// wrong way gives a model that runs and writes nonsense. So the two are pinned separately and against the
/// arithmetic written out rather than against each other.
/// </remarks>
public sealed class OnnxRotaryEmbeddingTests
{
    /// <summary>Halves are paired when the layout is not interleaved: value i turns with value i + half.</summary>
    [Fact]
    public void Rotate_pairs_the_halves_when_it_is_not_interleaved()
    {
        //Arrange
        var input = new[] { 1f, 2f, 3f, 4f, 5f, 6f };
        var cosine = new[] { 0.5f, 0.6f, 0.7f };
        var sine = new[] { 0.1f, 0.2f, 0.3f };
        var output = new float[6];

        //Act
        OnnxRotaryEmbedding.Rotate(input, 0, output, 0, cosine, sine, 0, 6, false);

        //Assert
        for (int i = 0; i < 3; i++)
        {
            output[i].Should().BeApproximately(
                (input[i] * cosine[i]) - (input[i + 3] * sine[i]), 1e-6f);
            output[i + 3].Should().BeApproximately(
                (input[i + 3] * cosine[i]) + (input[i] * sine[i]), 1e-6f);
        }
    }

    /// <summary>Neighbours are paired when the layout is interleaved: value 2i turns with value 2i + 1.</summary>
    [Fact]
    public void Rotate_pairs_the_neighbours_when_it_is_interleaved()
    {
        //Arrange
        var input = new[] { 1f, 2f, 3f, 4f, 5f, 6f };
        var cosine = new[] { 0.5f, 0.6f, 0.7f };
        var sine = new[] { 0.1f, 0.2f, 0.3f };
        var output = new float[6];

        //Act
        OnnxRotaryEmbedding.Rotate(input, 0, output, 0, cosine, sine, 0, 6, true);

        //Assert
        for (int i = 0; i < 3; i++)
        {
            output[2 * i].Should().BeApproximately(
                (input[2 * i] * cosine[i]) - (input[(2 * i) + 1] * sine[i]), 1e-6f);
            output[(2 * i) + 1].Should().BeApproximately(
                (input[(2 * i) + 1] * cosine[i]) + (input[2 * i] * sine[i]), 1e-6f);
        }
    }

    /// <summary>The two layouts really are different, so one cannot be mistaken for the other.</summary>
    [Fact]
    public void Rotate_gives_a_different_answer_for_each_layout()
    {
        //Arrange
        var input = new[] { 1f, 2f, 3f, 4f };
        var cosine = new[] { 0.5f, 0.6f };
        var sine = new[] { 0.1f, 0.2f };
        var halves = new float[4];
        var neighbours = new float[4];

        //Act
        OnnxRotaryEmbedding.Rotate(input, 0, halves, 0, cosine, sine, 0, 4, false);
        OnnxRotaryEmbedding.Rotate(input, 0, neighbours, 0, cosine, sine, 0, 4, true);

        //Assert
        halves.Should().NotBeEquivalentTo(neighbours);
    }

    /// <summary>Turning by an angle of nothing leaves every value where it was.</summary>
    [Fact]
    public void Rotate_by_nothing_is_the_identity()
    {
        //Arrange
        var input = new[] { 1f, -2f, 3.5f, 4f };
        var cosine = new[] { 1f, 1f };
        var sine = new[] { 0f, 0f };
        var output = new float[4];

        //Act
        OnnxRotaryEmbedding.Rotate(input, 0, output, 0, cosine, sine, 0, 4, false);

        //Assert
        output.Should().BeEquivalentTo(input);
    }

    /// <summary>A quarter turn of a pair is the pair swapped, with one of them negated.</summary>
    [Fact]
    public void Rotate_by_a_quarter_turn_swaps_the_pair()
    {
        //Arrange
        var input = new[] { 1f, 2f };
        var cosine = new[] { 0f };
        var sine = new[] { 1f };
        var output = new float[2];

        //Act
        OnnxRotaryEmbedding.Rotate(input, 0, output, 0, cosine, sine, 0, 2, false);

        //Assert
        output[0].Should().BeApproximately(-2f, 1e-6f);
        output[1].Should().BeApproximately(1f, 1e-6f);
    }

    /// <summary>The offsets say which row of which head to read and where to write it.</summary>
    [Fact]
    public void Rotate_reads_and_writes_at_the_offsets_it_is_given()
    {
        //Arrange
        var input = new float[8];
        Array.Fill(input, -1f);
        input[4] = 1f;
        input[5] = 2f;
        var cosine = new[] { 0f, 0f, 1f, 1f };
        var sine = new[] { 1f, 1f, 0f, 0f };
        var output = new float[8];

        //Act - the third row of the caches, which turns by nothing at all
        OnnxRotaryEmbedding.Rotate(input, 4, output, 2, cosine, sine, 2, 2, false);

        //Assert
        output[2].Should().BeApproximately(1f, 1e-6f);
        output[3].Should().BeApproximately(2f, 1e-6f);
        output[0].Should().Be(0f);
        output[7].Should().Be(0f);
    }
}

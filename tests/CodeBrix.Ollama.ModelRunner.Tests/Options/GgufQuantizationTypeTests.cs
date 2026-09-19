using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the public quantization types against the engine's own file-type enumeration.
/// </summary>
/// <remarks>
/// The public enumeration is handed to the native quantizer AS AN INTEGER: <c>QuantizeAsync</c> casts it
/// straight on to the binding's <see cref="LlamaFtype"/> and the engine reads whatever number arrives. A
/// member whose value drifted by one would therefore not fail to compile and would not throw - it would
/// quietly write a file of the wrong type. This is the fence that makes that impossible: every public member
/// is compared with the native member it stands for, and the two that are deliberately NOT exposed are named
/// so that adding one to the engine's list cannot pass unnoticed either.
/// </remarks>
public sealed class GgufQuantizationTypeTests
{
    /// <summary>
    /// Every public member and the native member it stands for. This table is the mapping; the tests below
    /// are what make it true.
    /// </summary>
    private static readonly IReadOnlyList<KeyValuePair<GgufQuantizationType, LlamaFtype>> Mapping =
        new[]
        {
            Pair(GgufQuantizationType.F32, LlamaFtype.AllF32),
            Pair(GgufQuantizationType.F16, LlamaFtype.MostlyF16),
            Pair(GgufQuantizationType.Q4_0, LlamaFtype.MostlyQ4_0),
            Pair(GgufQuantizationType.Q4_1, LlamaFtype.MostlyQ4_1),
            Pair(GgufQuantizationType.Q8_0, LlamaFtype.MostlyQ8_0),
            Pair(GgufQuantizationType.Q5_0, LlamaFtype.MostlyQ5_0),
            Pair(GgufQuantizationType.Q5_1, LlamaFtype.MostlyQ5_1),
            Pair(GgufQuantizationType.Q2_K, LlamaFtype.MostlyQ2K),
            Pair(GgufQuantizationType.Q3_K_S, LlamaFtype.MostlyQ3KS),
            Pair(GgufQuantizationType.Q3_K_M, LlamaFtype.MostlyQ3KM),
            Pair(GgufQuantizationType.Q3_K_L, LlamaFtype.MostlyQ3KL),
            Pair(GgufQuantizationType.Q4_K_S, LlamaFtype.MostlyQ4KS),
            Pair(GgufQuantizationType.Q4_K_M, LlamaFtype.MostlyQ4KM),
            Pair(GgufQuantizationType.Q5_K_S, LlamaFtype.MostlyQ5KS),
            Pair(GgufQuantizationType.Q5_K_M, LlamaFtype.MostlyQ5KM),
            Pair(GgufQuantizationType.Q6_K, LlamaFtype.MostlyQ6K),
            Pair(GgufQuantizationType.IQ2_XXS, LlamaFtype.MostlyIq2Xxs),
            Pair(GgufQuantizationType.IQ2_XS, LlamaFtype.MostlyIq2Xs),
            Pair(GgufQuantizationType.Q2_K_S, LlamaFtype.MostlyQ2KS),
            Pair(GgufQuantizationType.IQ3_XS, LlamaFtype.MostlyIq3Xs),
            Pair(GgufQuantizationType.IQ3_XXS, LlamaFtype.MostlyIq3Xxs),
            Pair(GgufQuantizationType.IQ1_S, LlamaFtype.MostlyIq1S),
            Pair(GgufQuantizationType.IQ4_NL, LlamaFtype.MostlyIq4Nl),
            Pair(GgufQuantizationType.IQ3_S, LlamaFtype.MostlyIq3S),
            Pair(GgufQuantizationType.IQ3_M, LlamaFtype.MostlyIq3M),
            Pair(GgufQuantizationType.IQ2_S, LlamaFtype.MostlyIq2S),
            Pair(GgufQuantizationType.IQ2_M, LlamaFtype.MostlyIq2M),
            Pair(GgufQuantizationType.IQ4_XS, LlamaFtype.MostlyIq4Xs),
            Pair(GgufQuantizationType.IQ1_M, LlamaFtype.MostlyIq1M),
            Pair(GgufQuantizationType.BF16, LlamaFtype.MostlyBf16),
            Pair(GgufQuantizationType.TQ1_0, LlamaFtype.MostlyTq1_0),
            Pair(GgufQuantizationType.TQ2_0, LlamaFtype.MostlyTq2_0),
            Pair(GgufQuantizationType.MXFP4_MOE, LlamaFtype.MostlyMxfp4Moe),
            Pair(GgufQuantizationType.Q1_0, LlamaFtype.MostlyQ1_0),
            Pair(GgufQuantizationType.Q2_0, LlamaFtype.MostlyQ2_0)
        };

    /// <summary>The two native file types the public enumeration deliberately leaves out.</summary>
    private static readonly LlamaFtype[] NotExposed = { LlamaFtype.MostlyNvfp4, LlamaFtype.Guessed };

    /// <summary>Every public member carries the number of the native member it stands for.</summary>
    [Fact]
    public void Every_member_carries_the_engines_own_number()
    {
        //Act and assert
        foreach (KeyValuePair<GgufQuantizationType, LlamaFtype> pair in Mapping)
        {
            ((int)pair.Key).Should().Be((int)pair.Value, "the type " + pair.Key + " is the engine's "
                + pair.Value);
        }
    }

    /// <summary>The mapping names every public member exactly once.</summary>
    [Fact]
    public void The_mapping_covers_every_public_member_once()
    {
        //Arrange
        var mapped = new HashSet<GgufQuantizationType>();
        foreach (KeyValuePair<GgufQuantizationType, LlamaFtype> pair in Mapping)
        {
            mapped.Add(pair.Key).Should().BeTrue("the type " + pair.Key + " is mapped once");
        }

        //Act
        GgufQuantizationType[] declared = Enum.GetValues<GgufQuantizationType>();

        //Assert
        declared.Should().HaveCount(mapped.Count);
        foreach (GgufQuantizationType value in declared)
        {
            mapped.Contains(value).Should().BeTrue("the type " + value + " is in the mapping");
        }
    }

    /// <summary>
    /// The public enumeration is exactly the native one minus the two members that are not file types a
    /// caller can ask the engine to write.
    /// </summary>
    /// <remarks>
    /// <c>Guessed</c> is what the engine records when a file does not say what it is, and NVFP4 is a type its
    /// quantizer has no mapping for at the vendored commit: asked for either, the native call refuses with
    /// "invalid output file type". They are left out for that reason and not by oversight, which is what this
    /// test says. A new type appearing in the engine's enumeration fails here until it is either exposed or
    /// listed beside those two.
    /// </remarks>
    [Fact]
    public void The_public_types_are_the_engines_types_minus_the_two_that_cannot_be_written()
    {
        //Arrange
        var publicValues = new HashSet<int>();
        foreach (GgufQuantizationType value in Enum.GetValues<GgufQuantizationType>())
        {
            publicValues.Add((int)value);
        }

        var excluded = new HashSet<int>();
        foreach (LlamaFtype value in NotExposed)
        {
            excluded.Add((int)value);
        }

        //Act
        var missing = new List<LlamaFtype>();
        foreach (LlamaFtype value in Enum.GetValues<LlamaFtype>())
        {
            if (!publicValues.Contains((int)value) && !excluded.Contains((int)value))
            {
                missing.Add(value);
            }
        }

        //Assert
        missing.Should().BeEmpty();
        publicValues.Should().HaveCount(Enum.GetValues<LlamaFtype>().Length - NotExposed.Length);
    }

    /// <summary>Names one pair of the mapping.</summary>
    /// <param name="exposed">The public member.</param>
    /// <param name="native">The native member it stands for.</param>
    /// <returns>The pair.</returns>
    private static KeyValuePair<GgufQuantizationType, LlamaFtype> Pair(
        GgufQuantizationType exposed, LlamaFtype native)
        => new KeyValuePair<GgufQuantizationType, LlamaFtype>(exposed, native);
}

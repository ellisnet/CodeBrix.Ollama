using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>Fusion respects observable tensors, dynamic shapes and output ownership.</summary>
public sealed class OnnxPlanOptimizerTests
{
    /// <summary>Only private, single-consumer intermediates disappear from the execution plan.</summary>
    [Theory]
    [InlineData("matmul_fusion_scalar", 1)]
    [InlineData("matmul_fusion_plain", 1)]
    [InlineData("matmul_fusion_shared_transpose", 0)]
    [InlineData("matmul_fusion_shared_scale", 0)]
    [InlineData("matmul_fusion_dynamic_quantize", 0)]
    public async Task Optimize_keeps_observable_intermediates(string name, int fusions)
    {
        //Arrange
        var fixture = OnnxFixtures.Load(name);

        //Act
        var plan = await OnnxGraphLoader.LoadAsync(
            OnnxModelLocation.ForFile(fixture.ModelPath), TestContext.Current.CancellationToken);

        //Assert
        plan.Nodes.Count(node => node.State is OnnxMatMulFusion).Should().Be(fusions);
        plan.Metadata.Operators.Should().Contain("Transpose");
        for (int i = 0; i < plan.Nodes.Length; i++) plan.Nodes[i].Index.Should().Be(i);
    }

    /// <summary>The fused result remains the caller's even after the model is run again.</summary>
    [Theory]
    [InlineData("matmul_fusion_scalar")]
    [InlineData("matmul_fusion_broadcast_fallback")]
    [InlineData("matmul_fusion_rank_fallback")]
    public async Task Run_preserves_retained_outputs(string name)
    {
        //Arrange
        var fixture = OnnxFixtures.Load(name);
        using var model = await OnnxModel.LoadAsync(fixture.ModelPath, null, TestContext.Current.CancellationToken);
        var first = model.Run(fixture.Inputs)["y"];
        var saved = (float[])first.Floats.Clone();
        var changed = new Dictionary<string, OnnxTensor>(fixture.Inputs);
        var original = changed["a"];
        changed["a"] = OnnxTensor.FromFloats(new float[original.Count], original.Shape.ToArray());

        //Act
        var second = model.Run(changed)["y"];

        //Assert
        first.Floats.Should().Equal(saved);
        first.Floats.Length.Should().Be((int)first.Count);
        second.Floats.Should().OnlyContain(value => value == 0f);
    }

    /// <summary>An incompatible dynamic rank still fails at the original transpose.</summary>
    [Fact]
    public async Task Run_validates_the_transpose_rank()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("matmul_fusion_scalar");
        using var model = await OnnxModel.LoadAsync(fixture.ModelPath, null, TestContext.Current.CancellationToken);
        var inputs = new Dictionary<string, OnnxTensor>(fixture.Inputs);
        inputs["b"] = OnnxTensor.FromFloats(inputs["b"].Floats, 27, 17);

        //Act
        Action act = () => model.Run(inputs);

        //Assert
        act.Should().Throw<InferenceException>().Which.Message.Should().Contain("Transpose");
    }

    /// <summary>A multiplier can change shape between calls without rebuilding the plan.</summary>
    [Fact]
    public async Task Run_switches_between_scalar_and_broadcast_scales()
    {
        //Arrange
        var scalar = OnnxFixtures.Load("matmul_fusion_scalar");
        var broadcast = OnnxFixtures.Load("matmul_fusion_broadcast_fallback");
        using var model = await OnnxModel.LoadAsync(scalar.ModelPath, null, TestContext.Current.CancellationToken);

        //Act
        model.Run(scalar.Inputs);
        var vectorResult = model.Run(broadcast.Inputs)["y"];
        var scalarResult = model.Run(scalar.Inputs)["y"];

        //Assert
        OnnxComparison.Compare(broadcast.Expected["y"], vectorResult).RelativeToLargest.Should().BeLessThan(1e-5);
        OnnxComparison.Compare(scalar.Expected["y"], scalarResult).RelativeToLargest.Should().BeLessThan(1e-5);
    }
}

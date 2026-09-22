using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

public sealed class OnnxOutputBindingTests
{
    [Theory]
    [InlineData("external_weights")]
    [InlineData("identity_float")]
    public async Task Allocated_and_aliased_graph_outputs_can_be_written_to_owned_buffers(string name)
    {
        var fixture = OnnxFixtures.Load(name);
        using IOnnxModel model = await OnnxModel.LoadAsync(fixture.ModelPath, cancellationToken: TestContext.Current.CancellationToken);
        var session = Assert.IsType<OnnxSession>(model);
        IReadOnlyDictionary<string, OnnxTensor> ordinary = await model.RunAsync(fixture.Inputs, TestContext.Current.CancellationToken);
        OnnxTensor target = OnnxTensor.FromFloats(new float[ordinary["y"].Count], ordinary["y"].Shape.ToArray());
        var buffers = new Dictionary<string, OnnxTensor> { ["y"] = target };
        var result = await session.RunIntoAsync(fixture.Inputs, buffers, TestContext.Current.CancellationToken);
        Assert.Same(target, result["y"]);
        Assert.Equal(ordinary["y"].Floats, target.Floats);
        Array.Fill(target.Floats, 99f);
        await session.RunIntoAsync(fixture.Inputs, buffers, TestContext.Current.CancellationToken);
        Assert.Equal(ordinary["y"].Floats, target.Floats);
        Assert.NotSame(ordinary["y"].Floats, target.Floats);
    }

    [Fact]
    public async Task Aliasing_unknown_outputs_and_wrong_shapes_are_refused_and_session_remains_usable()
    {
        var fixture = OnnxFixtures.Load("identity_float");
        using IOnnxModel model = await OnnxModel.LoadAsync(fixture.ModelPath, cancellationToken: TestContext.Current.CancellationToken);
        var session = Assert.IsType<OnnxSession>(model);
        var outputs = new Dictionary<string, OnnxTensor> { ["y"] = fixture.Inputs["a"] };
        await Assert.ThrowsAsync<ArgumentException>(() => session.RunIntoAsync(fixture.Inputs, outputs, TestContext.Current.CancellationToken));
        outputs["y"] = OnnxTensor.FromFloats(new float[6], 3, 2);
        await Assert.ThrowsAsync<InferenceException>(() => session.RunIntoAsync(fixture.Inputs, outputs, TestContext.Current.CancellationToken));
        outputs.Clear(); outputs["unknown"] = OnnxTensor.FromFloats(new float[6], 2, 3);
        await Assert.ThrowsAsync<ArgumentException>(() => session.RunIntoAsync(fixture.Inputs, outputs, TestContext.Current.CancellationToken));
        var result = await model.RunAsync(fixture.Inputs, TestContext.Current.CancellationToken);
        Assert.Equal(fixture.Inputs["a"].Floats, result["y"].Floats);
    }
}

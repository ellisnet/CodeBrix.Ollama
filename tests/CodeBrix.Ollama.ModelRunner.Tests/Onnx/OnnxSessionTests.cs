using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Runs every checked-in operator oracle through the managed interpreter and compares what it computes with
/// what ONNX Runtime computed from the same numbers.
/// </summary>
/// <remarks>
/// <para>
/// This is the suite that says the engine implements the SPECIFICATION rather than an idea of it. Each case
/// is a graph of one or a few nodes, so a failure names the operator; the sub-graph cases put several
/// together in the shapes a decoder actually uses - a root-mean-square normalization, a rotary embedding,
/// causal attention, a cached step whose past is empty - because operators that are each right on their own
/// can still be wrong about how they meet.
/// </para>
/// <para>
/// Every case is also run with buffer reuse switched off, on the portable vector path and on the scalar path,
/// so that the arena cannot be quietly corrupting a live tensor and a machine with no vector unit is as
/// correct as this one.
/// </para>
/// </remarks>
public sealed class OnnxSessionTests
{
    /// <summary>
    /// How far a float result may be from ONNX Runtime's, measured against the largest magnitude in the
    /// tensor. These graphs are small, so the real figures are far below it; the plan's own bar for a whole
    /// model is 1e-4.
    /// </summary>
    private const double Tolerance = 1e-5;

    /// <summary>The bar for one case, which is the strict one unless the case states its own.</summary>
    /// <param name="fixture">The case.</param>
    /// <returns>The largest relative difference that case is allowed.</returns>
    private static double BarFor(OnnxFixtureCase fixture) => fixture.Manifest.Tolerance ?? Tolerance;

    /// <summary>The engine computes what ONNX Runtime computes, on the widest kernels the processor offers.</summary>
    /// <param name="name">The oracle case.</param>
    [Theory]
    [MemberData(nameof(OnnxFixtures.AllCases), MemberType = typeof(OnnxFixtures))]
    public async Task Run_matches_onnxruntime(string name)
    {
        //Arrange
        var fixture = OnnxFixtures.Load(name);

        //Act
        var produced = await RunAsync(fixture, null);

        //Assert
        Expect(fixture, produced);
    }

    /// <summary>With buffer reuse switched off the engine computes exactly the same numbers, bit for bit.</summary>
    /// <param name="name">The oracle case.</param>
    [Theory]
    [MemberData(nameof(OnnxFixtures.AllCases), MemberType = typeof(OnnxFixtures))]
    public async Task Run_without_buffer_reuse_gives_identical_numbers(string name)
    {
        //Arrange
        var fixture = OnnxFixtures.Load(name);

        //Act
        var reusing = await RunAsync(fixture, new OnnxRunnerOptions { ReuseBuffers = true });
        var fresh = await RunAsync(fixture, new OnnxRunnerOptions { ReuseBuffers = false });

        //Assert
        Expect(fixture, fresh);
        foreach (var expected in fixture.Expected)
        {
            var comparison = OnnxComparison.Compare(reusing[expected.Key], fresh[expected.Key]);
            comparison.ShapeDifference.Should().BeNull();
            comparison.LargestDifference.Should().Be(0d, name + "/" + expected.Key + ": " + comparison);
            comparison.Mismatches.Should().Be(0, name + "/" + expected.Key + ": " + comparison);
        }
    }

    /// <summary>The scalar path, which is what a processor with no vector unit runs, agrees with the oracle.</summary>
    /// <param name="name">The oracle case.</param>
    [Theory]
    [MemberData(nameof(OnnxFixtures.AllCases), MemberType = typeof(OnnxFixtures))]
    public async Task Run_on_the_scalar_path_matches_onnxruntime(string name)
    {
        //Arrange
        var fixture = OnnxFixtures.Load(name);

        //Act
        var produced = await RunAsync(fixture, new OnnxRunnerOptions { KernelPath = OnnxKernelPath.Scalar });

        //Assert
        Expect(fixture, produced);
    }

    /// <summary>The portable vector path, which is what a machine without AVX2 runs, agrees with the oracle.</summary>
    /// <param name="name">The oracle case.</param>
    [Theory]
    [MemberData(nameof(OnnxFixtures.AllCases), MemberType = typeof(OnnxFixtures))]
    public async Task Run_on_the_vector_path_matches_onnxruntime(string name)
    {
        //Arrange
        var fixture = OnnxFixtures.Load(name);

        //Act
        var produced = await RunAsync(fixture, new OnnxRunnerOptions { KernelPath = OnnxKernelPath.Vector });

        //Assert
        Expect(fixture, produced);
    }

    /// <summary>Eight threads give the same answer as one.</summary>
    /// <param name="name">The oracle case.</param>
    [Theory]
    [MemberData(nameof(OnnxFixtures.ArithmeticCases), MemberType = typeof(OnnxFixtures))]
    public async Task Run_at_eight_threads_matches_one_thread(string name)
    {
        //Arrange
        var fixture = OnnxFixtures.Load(name);

        //Act
        var single = await RunAsync(fixture, new OnnxRunnerOptions { Threads = 1 });
        var many = await RunAsync(fixture, new OnnxRunnerOptions { Threads = 8 });

        //Assert
        foreach (var expected in fixture.Expected)
        {
            var comparison = OnnxComparison.Compare(single[expected.Key], many[expected.Key]);
            comparison.ShapeDifference.Should().BeNull();
            comparison.RelativeToLargest.Should().BeLessThan(1e-6, name + ": " + comparison);
            comparison.Mismatches.Should().Be(0, name + ": " + comparison);
        }
    }

    /// <summary>A second run of the same model gives exactly what the first gave: nothing is carried over.</summary>
    /// <param name="name">The oracle case.</param>
    [Theory]
    [MemberData(nameof(OnnxFixtures.ArithmeticCases), MemberType = typeof(OnnxFixtures))]
    public async Task Run_twice_gives_identical_numbers(string name)
    {
        //Arrange
        var fixture = OnnxFixtures.Load(name);
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);

        //Act
        var first = model.Run(fixture.Inputs);
        var second = model.Run(fixture.Inputs);

        //Assert
        foreach (var expected in fixture.Expected)
        {
            var comparison = OnnxComparison.Compare(first[expected.Key], second[expected.Key]);
            comparison.LargestDifference.Should().Be(0d, name + ": " + comparison);
            comparison.Mismatches.Should().Be(0, name + ": " + comparison);
        }
    }

    /// <summary>A tensor a run hands back owns an array of exactly its own length, so it can be fed straight back.</summary>
    [Fact]
    public async Task Run_hands_back_tensors_that_own_their_arrays()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("subgraph_empty_step");
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);

        //Act
        var produced = model.Run(fixture.Inputs);

        //Assert
        produced["present"].Floats.Length.Should().Be((int)produced["present"].Count);
    }

    /// <summary>
    /// A decode step's cache is fed back without being copied: the tensor the first step handed out is the
    /// very tensor the second step is given, and the second step's cache grows by the positions it added.
    /// </summary>
    [Fact]
    public async Task Run_takes_its_own_output_back_as_an_input()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("subgraph_empty_step");
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);
        var first = model.Run(fixture.Inputs);

        //Act
        var second = model.Run(new Dictionary<string, OnnxTensor>(StringComparer.Ordinal)
        {
            ["past"] = first["present"],
            ["fresh"] = fixture.Inputs["fresh"],
        });

        //Assert
        second["present"].Shape[2].Should().Be(6L);
    }

    /// <summary>What the graph says about itself comes through on the loaded model.</summary>
    [Fact]
    public async Task Metadata_describes_the_graph()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("subgraph_attention");

        //Act
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);

        //Assert
        model.Metadata.Inputs.Should().HaveCount(3);
        model.Metadata.Outputs.Should().HaveCount(1);
        model.Metadata.Inputs[0].ElementType.Should().Be(OnnxElementType.Float);
        model.Metadata.Operators.Should().Contain("Softmax");
        model.Metadata.Opsets.Should().HaveCount(1);
        model.Metadata.Opsets[0].Version.Should().Be(14L);
    }

    /// <summary>A graph's symbolic dimensions are reported as symbols rather than resolved to a number.</summary>
    [Fact]
    public async Task Metadata_reports_a_fixed_shape_as_fixed()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");

        //Act
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);

        //Assert
        model.Metadata.Inputs[0].Shape.Should().HaveCount(3);
        model.Metadata.Inputs[0].Shape[0].IsFixed.Should().BeTrue();
        model.Metadata.Inputs[0].Shape[0].Length.Should().Be(2L);
    }

    /// <summary>The options a model was loaded with come back with the thread count resolved to a number.</summary>
    [Fact]
    public async Task Options_come_back_with_the_thread_count_resolved()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");

        //Act
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);

        //Assert
        model.Options.Threads.Should().NotBeNull();
        model.Options.Threads.Value.Should().BeGreaterThan(0);
    }

    /// <summary>Concurrent runs on one model either succeed with the right numbers or are refused; none corrupts another.</summary>
    [Fact]
    public async Task Run_refuses_a_second_run_rather_than_corrupting_the_first()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("matmul_constant_weight_wide");
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);
        var failures = new List<Exception>();
        var results = new List<IReadOnlyDictionary<string, OnnxTensor>>();

        //Act
        var workers = new Task[8];
        for (int i = 0; i < workers.Length; i++)
        {
            workers[i] = Task.Run(
                () =>
                {
                    for (int attempt = 0; attempt < 25; attempt++)
                    {
                        try
                        {
                            var produced = model.Run(fixture.Inputs);
                            lock (results) results.Add(produced);
                        }
                        catch (Exception exception)
                        {
                            lock (failures) failures.Add(exception);
                        }
                    }
                },
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(workers);

        //Assert
        foreach (var failure in failures) failure.Should().BeOfType<InferenceException>();
        results.Should().NotBeEmpty();
        foreach (var produced in results)
        {
            var comparison = OnnxComparison.Compare(fixture.Expected["y"], produced["y"]);
            comparison.RelativeToLargest.Should().BeLessThan(BarFor(fixture), comparison.ToString());
        }
    }

    /// <summary>A run after the model has been disposed says so.</summary>
    [Fact]
    public async Task Run_after_Dispose_throws()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");
        var model = await OnnxModel.LoadAsync(fixture.ModelPath, null, TestContext.Current.CancellationToken);
        await model.DisposeAsync();

        //Act
        Action act = () => model.Run(fixture.Inputs);

        //Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>Disposing twice is not an error.</summary>
    [Fact]
    public async Task DisposeAsync_twice_is_harmless()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");
        var model = await OnnxModel.LoadAsync(fixture.ModelPath, null, TestContext.Current.CancellationToken);

        //Act
        await model.DisposeAsync();
        Func<Task> act = async () => await model.DisposeAsync();

        //Assert
        await act.Should().NotThrowAsync();
    }

    /// <summary>A missing input is named.</summary>
    [Fact]
    public async Task Run_without_an_input_throws()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);

        //Act
        Action act = () => model.Run(new Dictionary<string, OnnxTensor>(StringComparer.Ordinal));

        //Assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("'a'");
    }

    /// <summary>An input the graph does not declare is named rather than ignored.</summary>
    [Fact]
    public async Task Run_with_an_input_the_graph_does_not_take_throws()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);
        var inputs = new Dictionary<string, OnnxTensor>(fixture.Inputs, StringComparer.Ordinal)
        {
            ["surplus"] = OnnxTensor.FromFloats(new[] { 1f }, 1),
        };

        //Act
        Action act = () => model.Run(inputs);

        //Assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("surplus");
    }

    /// <summary>An input of the wrong element type is refused before anything is computed.</summary>
    [Fact]
    public async Task Run_with_the_wrong_element_type_throws()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);
        var inputs = new Dictionary<string, OnnxTensor>(fixture.Inputs, StringComparer.Ordinal)
        {
            ["a"] = OnnxTensor.FromInt64(new long[24], 2, 3, 4),
        };

        //Act
        Action act = () => model.Run(inputs);

        //Assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("int64");
    }

    /// <summary>Shapes that do not meet are an inference failure that names the node.</summary>
    [Fact]
    public async Task Run_with_shapes_that_do_not_meet_names_the_node()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("matmul_2d");
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);
        var inputs = new Dictionary<string, OnnxTensor>(StringComparer.Ordinal)
        {
            ["a"] = OnnxTensor.FromFloats(new float[6], 3, 2),
            ["b"] = OnnxTensor.FromFloats(new float[20], 5, 4),
        };

        //Act
        Action act = () => model.Run(inputs);

        //Assert
        act.Should().Throw<InferenceException>().Which.Message.Should().Contain("MatMul");
    }

    /// <summary>Running with a null set of inputs says so rather than failing somewhere inside.</summary>
    [Fact]
    public async Task Run_with_null_inputs_throws()
    {
        //Arrange
        var fixture = OnnxFixtures.Load("add_same_float");
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, null, TestContext.Current.CancellationToken);

        //Act
        Action act = () => model.Run(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private static async Task<IReadOnlyDictionary<string, OnnxTensor>> RunAsync(
        OnnxFixtureCase fixture, OnnxRunnerOptions options)
    {
        await using var model = await OnnxModel.LoadAsync(
            fixture.ModelPath, options, TestContext.Current.CancellationToken);
        return await model.RunAsync(fixture.Inputs, TestContext.Current.CancellationToken);
    }

    private static void Expect(OnnxFixtureCase fixture, IReadOnlyDictionary<string, OnnxTensor> produced)
    {
        produced.Should().HaveCount(fixture.Expected.Count);
        foreach (var expected in fixture.Expected)
        {
            produced.Should().ContainKey(expected.Key);
            var comparison = OnnxComparison.Compare(expected.Value, produced[expected.Key]);
            comparison.ShapeDifference.Should().BeNull(fixture.Name + "/" + expected.Key);
            comparison.RelativeToLargest.Should().BeLessThan(
                BarFor(fixture), fixture.Name + "/" + expected.Key + ": " + comparison);
            comparison.Mismatches.Should().Be(
                0, fixture.Name + "/" + expected.Key + ": " + comparison);
        }
    }
}

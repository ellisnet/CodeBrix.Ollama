using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// What the loader leaves behind: an execution plan whose weights are in the layout the kernels want, with
/// the stored layout let go, and no state machine holding a model's worth of bytes.
/// </summary>
/// <remarks>
/// Both of the tests here fence a defect that was MEASURED rather than reasoned about, on the 822 MB graph
/// this engine was built for. Neither shows up as a wrong answer; both show up as a gigabyte.
/// </remarks>
public sealed class OnnxGraphLoaderTests
{
    /// <summary>
    /// The load is a synchronous method behind one background call, and not an <c>async</c> one.
    /// </summary>
    /// <remarks>
    /// THIS IS A MEMORY FENCE, not a style rule. A local of an async method does not live on the stack: the
    /// compiler puts it in a state-machine object, and for a method that actually suspends, that object IS
    /// the task it returns. So a local holding the file's bytes stays reachable for as long as the CALLER
    /// holds the task it awaited - which, inside another async method, is until that method finishes. On the
    /// larger of the two graphs this engine was built for, that was 784 MiB held for the life of the model,
    /// measured with the collector asked twice. Making the load synchronous inside a single
    /// <c>Task.Run</c> puts every one of those locals back on the stack.
    /// </remarks>
    [Fact]
    public void Load_does_not_hold_the_file_in_a_state_machine()
    {
        //Arrange
        var loader = typeof(OnnxModel).Assembly.GetType(
            "CodeBrix.Ollama.ModelRunner.OnnxGraphLoader", throwOnError: true);

        //Act
        var asynchronous = loader
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
                | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<AsyncStateMachineAttribute>() != null)
            .Select(method => method.Name)
            .ToArray();

        //Assert
        asynchronous.Should().BeEmpty();
    }

    /// <summary>
    /// A constant matrix-multiply weight is taken into the kernel's own layout and the stored one is let go,
    /// so a loaded model holds ONE copy of its weights rather than two.
    /// </summary>
    /// <returns>A task that completes when the graph has been loaded and inspected.</returns>
    [Fact]
    public async Task Load_drops_the_stored_layout_of_a_weight_a_kernel_took()
    {
        //Arrange
        var directory = Path.Combine(OnnxFixtures.Directory, "subgraph_feed_forward");
        var location = OnnxModelLocation.ForDirectory(directory, "model.onnx");

        //Act
        var plan = await OnnxGraphLoader.LoadAsync(location, TestContext.Current.CancellationToken);

        //Assert
        var weights = plan.Slots.Where(slot => slot.Kind == OnnxSlotKind.Initializer).ToArray();
        weights.Should().HaveCount(2);
        foreach (var weight in weights)
        {
            weight.FoldedIntoKernels.Should().BeTrue(weight.Name);
            weight.Initializer.Should().BeNull(weight.Name);
        }

        plan.Nodes.Count(node => node.State is OnnxPackedWeight).Should().Be(2);
    }

    /// <summary>A weight nothing folds is kept, because the run still has to read it.</summary>
    /// <returns>A task that completes when the graph has been loaded and inspected.</returns>
    [Fact]
    public async Task Load_keeps_a_weight_no_kernel_took()
    {
        //Arrange
        var directory = Path.Combine(OnnxFixtures.Directory, "subgraph_rms_norm");
        var location = OnnxModelLocation.ForDirectory(directory, "model.onnx");

        //Act
        var plan = await OnnxGraphLoader.LoadAsync(location, TestContext.Current.CancellationToken);

        //Assert
        var weights = plan.Slots.Where(slot => slot.Kind == OnnxSlotKind.Initializer).ToArray();
        weights.Should().NotBeEmpty();
        foreach (var weight in weights)
        {
            weight.Initializer.Should().NotBeNull(weight.Name);
        }
    }

    /// <summary>
    /// A block-quantized weight is kept EXACTLY AS THE FILE HOLDS IT - one byte for every two four-bit values
    /// - and is never turned into floats.
    /// </summary>
    /// <remarks>
    /// THIS IS A MEMORY FENCE, and it is the one the whole quantized path exists for. Expanding a four-bit
    /// weight at load time would give the right numbers and undo the only reason the graph was reduced: a
    /// model that the store made a quarter of the size would take four times that again to run. The check is
    /// on the BYTE COUNT rather than on the answers, because the answers would not notice.
    /// </remarks>
    /// <returns>A task that completes when the graph has been loaded and inspected.</returns>
    [Fact]
    public async Task Load_keeps_a_block_quantized_weight_packed()
    {
        //Arrange
        var directory = Path.Combine(OnnxFixtures.Directory, "matmul_nbits_4bit_block32");
        var location = OnnxModelLocation.ForDirectory(directory, "model.onnx");

        //Act
        var plan = await OnnxGraphLoader.LoadAsync(location, TestContext.Current.CancellationToken);

        //Assert
        var weight = plan.Nodes.Select(node => node.State).OfType<OnnxBlockQuantizedWeight>().Single();
        weight.Bits.Should().Be(4);
        weight.BlockSize.Should().Be(32);

        // Two values to a byte, and not one float to a value: the packed array is an eighth of what the
        // dequantized weight would be.
        var values = (long)weight.Width * weight.Reduction;
        weight.Packed.Length.Should().Be((int)(values / 2));
        weight.Scales.Length.Should().Be(weight.Width * weight.BlockCount);
        foreach (var slot in plan.Slots.Where(slot => slot.Kind == OnnxSlotKind.Initializer))
        {
            slot.Initializer.Should().BeNull(slot.Name);
            slot.FoldedIntoKernels.Should().BeTrue(slot.Name);
        }
    }

    /// <summary>
    /// An 8-bit weight of a <c>MatMulInteger</c> is turned round and kept one byte per element, not widened.
    /// </summary>
    /// <returns>A task that completes when the graph has been loaded and inspected.</returns>
    [Fact]
    public async Task Load_keeps_an_integer_weight_one_byte_per_element()
    {
        //Arrange
        var directory = Path.Combine(OnnxFixtures.Directory, "matmul_integer_signed_weight");
        var location = OnnxModelLocation.ForDirectory(directory, "model.onnx");

        //Act
        var plan = await OnnxGraphLoader.LoadAsync(location, TestContext.Current.CancellationToken);

        //Assert
        var weight = plan.Nodes.Select(node => node.State).OfType<OnnxIntegerWeight>().Single();
        weight.Values.Length.Should().Be(weight.Reduction * weight.Width);
        weight.ZeroPoints.Should().HaveCount(1);
    }

    /// <summary>The plan says, for every node, which tensors can go back to the arena once it has run.</summary>
    /// <returns>A task that completes when the graph has been loaded and inspected.</returns>
    [Fact]
    public async Task Load_works_out_when_each_tensor_is_last_read()
    {
        //Arrange
        var directory = Path.Combine(OnnxFixtures.Directory, "subgraph_attention");
        var location = OnnxModelLocation.ForDirectory(directory, "model.onnx");

        //Act
        var plan = await OnnxGraphLoader.LoadAsync(location, TestContext.Current.CancellationToken);

        //Assert
        plan.Nodes.Sum(node => node.Release.Length).Should().BeGreaterThan(0);
        foreach (var slot in plan.Slots)
        {
            if (slot.IsGraphOutput) plan.Nodes.Any(node => node.Release.Contains(slot.Index)).Should().BeFalse();
        }
    }
}

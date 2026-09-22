namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a kernel is given while it runs: the node, whatever that node's kernel worked out at load time, the
/// tensors it reads, somewhere to put the tensors it writes, and the settings the model was loaded with.
/// </summary>
/// <remarks>
/// One context serves a whole run and is pointed at each node in turn, so running a thousand-node graph
/// allocates one of these and not a thousand.
/// </remarks>
internal sealed class OnnxOperatorContext
{
    private readonly OnnxValue[] _values;
    private readonly OnnxTensor[] _outputBuffers;

    /// <summary>Creates the context one run uses.</summary>
    /// <param name="values">The run's slot table.</param>
    /// <param name="arena">The arena tensors are allocated from.</param>
    /// <param name="settings">The resolved options.</param>
    /// <param name="outputBuffers">Optional internal caller-owned output buffers, indexed by plan slot.</param>
    internal OnnxOperatorContext(OnnxValue[] values, OnnxArena arena, OnnxExecutionSettings settings, OnnxTensor[] outputBuffers = null)
    {
        _values = values;
        Arena = arena;
        Settings = settings;
        _outputBuffers = outputBuffers;
    }

    /// <summary>The node being run.</summary>
    internal OnnxPlanNode Node { get; private set; }

    /// <summary>Whatever this node's kernel worked out when the model was loaded.</summary>
    internal object State => Node.State;

    /// <summary>The arena tensors are allocated from.</summary>
    internal OnnxArena Arena { get; }

    /// <summary>The resolved options: the thread count and the arithmetic path.</summary>
    internal OnnxExecutionSettings Settings { get; }

    /// <summary>How many inputs the node declares, including any it left out.</summary>
    internal int InputCount => Node.Inputs.Length;

    /// <summary>How many outputs the node declares.</summary>
    internal int OutputCount => Node.Outputs.Length;

    /// <summary>Points the context at the next node.</summary>
    /// <param name="node">The node about to run.</param>
    internal void Bind(OnnxPlanNode node) => Node = node;

    /// <summary>
    /// The tensor of one input, or <see langword="null"/> when the node left that input out or its kernel
    /// folded it into its own state at load time.
    /// </summary>
    /// <param name="index">The input's position.</param>
    /// <returns>The tensor, or <see langword="null"/>.</returns>
    internal OnnxValue Input(int index)
    {
        if (index < 0 || index >= Node.Inputs.Length) return null;
        int slot = Node.Inputs[index];
        return slot < 0 ? null : _values[slot];
    }

    /// <summary>The tensor of one input that the node is required to have.</summary>
    /// <param name="index">The input's position.</param>
    /// <returns>The tensor.</returns>
    /// <exception cref="InferenceException">The node left that input out.</exception>
    internal OnnxValue RequireInput(int index)
    {
        OnnxValue value = Input(index);
        if (value == null)
        {
            throw Fail("input " + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " is not there.");
        }

        return value;
    }

    /// <summary>Whether the node keeps a given output at all.</summary>
    /// <param name="index">The output's position.</param>
    /// <returns><see langword="true"/> when something reads it.</returns>
    internal bool WantsOutput(int index) =>
        index >= 0 && index < Node.Outputs.Length && Node.Outputs[index] >= 0;

    /// <summary>
    /// Allocates one of the node's outputs and puts it in its slot, taking a buffer that can never go back to
    /// the arena when the graph is going to hand this tensor to the caller.
    /// </summary>
    /// <param name="index">The output's position.</param>
    /// <param name="elementType">The element type.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The tensor, whose elements are whatever the buffer last held.</returns>
    internal OnnxValue AllocateOutput(int index, OnnxElementType elementType, long[] shape)
    {
        int slot = Node.Outputs[index];
        OnnxTensor destination = slot < 0 ? null : _outputBuffers?[slot];
        OnnxValue value;
        if (destination != null)
        {
            OnnxOutputBinding.Validate(destination, elementType, shape);
            value = OnnxValue.Wrap(elementType, destination.Data(), shape);
        }
        else value = OnnxValue.Allocate(Arena, elementType, shape, !Node.OutputIsGraphOutput[index]);
        Store(index, value);
        return value;
    }

    /// <summary>Puts an already-built tensor in one of the node's output slots.</summary>
    /// <param name="index">The output's position.</param>
    /// <param name="value">The tensor.</param>
    internal void SetOutput(int index, OnnxValue value) => Store(index, value);

    /// <summary>Builds the exception for a node that cannot run on the tensors it was given.</summary>
    /// <param name="reason">What is wrong, as a sentence fragment.</param>
    /// <returns>The exception, for the caller to throw.</returns>
    internal InferenceException Fail(string reason) =>
        new InferenceException(Node.Describe() + ": " + reason);

    private void Store(int index, OnnxValue value)
    {
        int slot = Node.Outputs[index];
        if (slot < 0)
        {
            // Nothing reads this output. The tensor was still built, because a kernel that produces two
            // results usually cannot produce one of them alone, so it goes straight back to the arena.
            value.Release(Arena);
            return;
        }

        _values[slot] = value;
    }
}

using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One node of an execution plan: the kernel that runs it, the slots it reads and writes, whatever the
/// kernel worked out at load time, and the slots whose buffers can go back to the arena once it has run.
/// </summary>
internal sealed class OnnxPlanNode
{
    /// <summary>Creates a node.</summary>
    /// <param name="index">Its position in the plan.</param>
    /// <param name="opType">The operator type.</param>
    /// <param name="name">The node's name in the file, which may be empty.</param>
    /// <param name="kernel">The kernel that runs it.</param>
    /// <param name="inputs">
    /// The slot of each input, with -1 for one the node left out and for a weight the kernel folded into its
    /// own state at load time.
    /// </param>
    /// <param name="outputs">The slot of each output, with -1 for one the node discards.</param>
    internal OnnxPlanNode(int index, string opType, string name, OnnxKernel kernel, int[] inputs, int[] outputs)
    {
        Index = index;
        OpType = opType;
        Name = name;
        Kernel = kernel;
        Inputs = inputs;
        Outputs = outputs;
        OutputIsGraphOutput = new bool[outputs.Length];
        Release = Array.Empty<int>();
    }

    /// <summary>Its position in the plan.</summary>
    internal int Index { get; }

    /// <summary>The operator type.</summary>
    internal string OpType { get; }

    /// <summary>The node's name in the file, which may be empty.</summary>
    internal string Name { get; }

    /// <summary>The kernel that runs it.</summary>
    internal OnnxKernel Kernel { get; }

    /// <summary>The slot of each input, with -1 for one that is absent or folded into the kernel's state.</summary>
    internal int[] Inputs { get; }

    /// <summary>The slot of each output, with -1 for one the node discards.</summary>
    internal int[] Outputs { get; }

    /// <summary>Whether each output is one the graph hands back, which is what keeps its buffer out of the pool.</summary>
    internal bool[] OutputIsGraphOutput { get; }

    /// <summary>Whatever the kernel worked out when the model was loaded: an axis, a permutation, a packed weight.</summary>
    internal object State { get; set; }

    /// <summary>The slots whose buffers can go back to the arena once this node has run.</summary>
    internal int[] Release { get; set; }

    /// <summary>Names the node for a message, by its own name when it has one and by its position otherwise.</summary>
    /// <returns>A short description, for example <c>Slice node '/model/Slice_3'</c>.</returns>
    internal string Describe() => string.IsNullOrEmpty(Name)
        ? OpType + " node " + Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : OpType + " node '" + Name + "'";
}

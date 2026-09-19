using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One ONNX operator: what it is called, what it will accept, and what it computes.
/// </summary>
/// <remarks>
/// <para>
/// A kernel is stateless and there is one instance of each for the life of the process. Everything that
/// belongs to a particular NODE - an axis, a permutation, a weight turned round into the layout the matrix
/// kernel wants - is worked out once in <see cref="Prepare"/>, when the model is loaded, and kept on the plan
/// node. That is also where anything the engine cannot do is refused, so a graph that will not run says so
/// while it is being loaded rather than half way through a generation.
/// </para>
/// <para>
/// The counts and <see cref="Attributes"/> below are checked by the loader before <see cref="Prepare"/> is
/// called, so every kernel gets the same refusals for the same reasons and none has to write them out. An
/// attribute a kernel does not name is REFUSED rather than ignored: an attribute quietly dropped changes what
/// the graph computes, and the difference would show up as a wrong answer rather than as an error.
/// </para>
/// <para>
/// <see cref="Run"/> is called on one thread. A kernel that wants more spreads its own work, from the thread
/// count on the context's settings.
/// </para>
/// </remarks>
internal abstract class OnnxKernel
{
    /// <summary>The operator type this kernel answers for, spelled as the ONNX specification spells it.</summary>
    internal abstract string OpType { get; }

    /// <summary>
    /// The operator domain, empty for the standard <c>ai.onnx</c> set and
    /// <see cref="OnnxKernels.ContributedDomain"/> for an operator a runtime vendor contributed.
    /// </summary>
    /// <remarks>
    /// A contributed operator is a different operator from a standard one of the same name, so the registry is
    /// keyed by BOTH. <c>SimplifiedLayerNormalization</c> is the awkward case and it is deliberate: the model
    /// builders emit it in the DEFAULT domain although no ONNX release defines it there, so that is where the
    /// engine answers for it.
    /// </remarks>
    internal virtual string Domain => string.Empty;

    /// <summary>The fewest inputs a node of this operator may declare.</summary>
    internal virtual int MinInputs => 1;

    /// <summary>The most inputs a node of this operator may declare.</summary>
    internal virtual int MaxInputs => 1;

    /// <summary>The fewest outputs a node of this operator may declare.</summary>
    internal virtual int MinOutputs => 1;

    /// <summary>The most outputs a node of this operator may declare.</summary>
    internal virtual int MaxOutputs => 1;

    /// <summary>The attribute names this kernel reads. Anything else on the node is refused.</summary>
    internal virtual string[] Attributes => Array.Empty<string>();

    /// <summary>
    /// Reads the node's attributes when the model is loaded, refuses anything the kernel cannot do, and
    /// returns whatever <see cref="Run"/> will want to have ready.
    /// </summary>
    /// <param name="context">The node as the file has it, and the weights that have been read so far.</param>
    /// <returns>The state to keep on the plan node, or <see langword="null"/> when the kernel needs none.</returns>
    internal virtual object Prepare(OnnxNodeLoadContext context) => null;

    /// <summary>Computes the node's outputs from its inputs.</summary>
    /// <param name="context">The node being run.</param>
    internal abstract void Run(OnnxOperatorContext context);
}

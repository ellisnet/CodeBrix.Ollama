using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One node as the file has it, handed to a kernel while the model is being loaded: its attributes, the
/// operator-set version the graph is written against, and the weights its inputs already point at.
/// </summary>
/// <remarks>
/// A kernel reads what it needs here and refuses what it cannot do. It can also FOLD a weight - take the
/// tensor an input names into its own state, in whatever layout it wants to compute in - and the loader then
/// stops feeding that input at run time and releases the original if no other node wanted it.
/// </remarks>
internal sealed class OnnxNodeLoadContext
{
    private readonly OnnxNodeProto _node;
    private readonly OnnxPlanSlot[] _slots;
    private readonly int[] _inputSlots;
    private readonly int[] _initializerReaders;

    /// <summary>Creates the context for one node.</summary>
    /// <param name="node">The node as the file has it.</param>
    /// <param name="index">Its position in the graph.</param>
    /// <param name="opset">The version of the standard operator set the graph is written against.</param>
    /// <param name="slots">The plan's slot table.</param>
    /// <param name="inputSlots">This node's input slots, which folding rewrites.</param>
    /// <param name="initializerReaders">How many nodes still read each slot's weight.</param>
    internal OnnxNodeLoadContext(
        OnnxNodeProto node,
        int index,
        long opset,
        OnnxPlanSlot[] slots,
        int[] inputSlots,
        int[] initializerReaders)
    {
        _node = node;
        _slots = slots;
        _inputSlots = inputSlots;
        _initializerReaders = initializerReaders;
        Index = index;
        Opset = opset;
    }

    /// <summary>The node's position in the graph.</summary>
    internal int Index { get; }

    /// <summary>The version of the standard operator set the graph is written against.</summary>
    internal long Opset { get; }

    /// <summary>
    /// How many bytes of stored weight this node's preparation let go of, which the load counts so that a
    /// model's worth of abandoned layouts does not pile up behind the collector.
    /// </summary>
    internal long ReleasedBytes { get; private set; }

    /// <summary>The operator type.</summary>
    internal string OpType => _node.OpType;

    /// <summary>The node's name in the file, which may be empty.</summary>
    internal string NodeName => _node.Name;

    /// <summary>How many inputs the node declares, including any it names as empty.</summary>
    internal int InputCount => _inputSlots.Length;

    /// <summary>How many outputs the node declares.</summary>
    internal int OutputCount => _node.Outputs.Count;

    /// <summary>Whether the node supplies a given input at all.</summary>
    /// <param name="index">The input's position.</param>
    /// <returns><see langword="true"/> when it names one.</returns>
    internal bool HasInput(int index) => index >= 0 && index < _inputSlots.Length && _inputSlots[index] >= 0;

    /// <summary>Whether the node keeps a given output at all, rather than naming it as empty.</summary>
    /// <param name="index">The output's position.</param>
    /// <returns><see langword="true"/> when it names one.</returns>
    internal bool HasOutput(int index) =>
        index >= 0 && index < _node.Outputs.Count && !string.IsNullOrEmpty(_node.Outputs[index]);

    /// <summary>Refuses everything but the attributes a kernel understands, so none is silently ignored.</summary>
    /// <param name="names">The attribute names this kernel reads.</param>
    /// <exception cref="ModelLoadException">The node carries an attribute that is not among them.</exception>
    internal void AllowAttributes(params string[] names)
    {
        foreach (OnnxAttributeProto attribute in _node.Attributes)
        {
            bool known = false;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(attribute.Name, names[i], StringComparison.Ordinal))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                throw Refuse(
                    "it carries the attribute '" + attribute.Name
                    + "', which this engine does not implement for " + OpType
                    + "; ignoring it would change what the graph computes");
            }
        }
    }

    /// <summary>Requires that the node declare between so many and so many inputs.</summary>
    /// <param name="least">The fewest it may declare.</param>
    /// <param name="most">The most it may declare.</param>
    /// <exception cref="ModelLoadException">It declares a number outside that range.</exception>
    internal void RequireInputs(int least, int most)
    {
        if (InputCount < least || InputCount > most)
        {
            throw Refuse(
                "it declares " + InputCount.ToString(CultureInfo.InvariantCulture) + " inputs and "
                + OpType + " takes " + Range(least, most));
        }
    }

    /// <summary>Requires that the node declare between so many and so many outputs.</summary>
    /// <param name="least">The fewest it may declare.</param>
    /// <param name="most">The most it may declare.</param>
    /// <exception cref="ModelLoadException">It declares a number outside that range.</exception>
    internal void RequireOutputs(int least, int most)
    {
        if (OutputCount < least || OutputCount > most)
        {
            throw Refuse(
                "it declares " + OutputCount.ToString(CultureInfo.InvariantCulture) + " outputs and "
                + OpType + " produces " + Range(least, most));
        }
    }

    /// <summary>An integer attribute, or a fallback when the node does not carry it.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="fallback">What the specification says it defaults to.</param>
    /// <returns>The value.</returns>
    internal long Int(string name, long fallback)
    {
        OnnxAttributeProto attribute = _node.FindAttribute(name);
        if (attribute == null || !attribute.Int.HasValue) return fallback;
        return attribute.Int.Value;
    }

    /// <summary>A list-of-integers attribute, or an empty list when the node does not carry it.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <returns>The values.</returns>
    internal long[] Ints(string name)
    {
        OnnxAttributeProto attribute = _node.FindAttribute(name);
        return attribute == null ? Array.Empty<long>() : attribute.Ints.ToArray();
    }

    /// <summary>A list-of-floats attribute, or an empty list when the node does not carry it.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <returns>The values.</returns>
    internal float[] Floats(string name)
    {
        OnnxAttributeProto attribute = _node.FindAttribute(name);
        return attribute == null ? Array.Empty<float>() : attribute.Floats.ToArray();
    }

    /// <summary>A text attribute, or <see langword="null"/> when the node does not carry one.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <returns>The value, read as UTF-8, or <see langword="null"/>.</returns>
    internal string Text(string name)
    {
        OnnxAttributeProto attribute = _node.FindAttribute(name);
        return attribute?.Bytes == null ? null : System.Text.Encoding.UTF8.GetString(attribute.Bytes);
    }

    /// <summary>Whether the node carries an attribute of a given name.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    internal bool HasAttribute(string name) => _node.FindAttribute(name) != null;

    /// <summary>A float attribute, or a fallback when the node does not carry it.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="fallback">What the specification says it defaults to.</param>
    /// <returns>The value.</returns>
    internal float Float(string name, float fallback)
    {
        OnnxAttributeProto attribute = _node.FindAttribute(name);
        if (attribute == null || !attribute.Float.HasValue) return fallback;
        return attribute.Float.Value;
    }

    /// <summary>A tensor attribute read into a tensor the engine can compute with.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <returns>The tensor, or <see langword="null"/> when the node does not carry the attribute.</returns>
    /// <exception cref="ModelLoadException">The tensor is of a type the engine does not compute in.</exception>
    internal OnnxValue Tensor(string name)
    {
        OnnxAttributeProto attribute = _node.FindAttribute(name);
        if (attribute?.Tensor == null) return null;

        if (attribute.Tensor.HasExternalData)
        {
            throw Refuse("its '" + name + "' attribute points at a side file that was not resolved");
        }

        return OnnxTensorReader.Read(attribute.Tensor, Describe() + ": its '" + name + "' attribute");
    }

    /// <summary>
    /// The weight one of the node's inputs names, when that input is a weight rather than something computed.
    /// </summary>
    /// <param name="index">The input's position.</param>
    /// <returns>The weight, or <see langword="null"/> when the input is computed or absent.</returns>
    internal OnnxValue Initializer(int index)
    {
        if (!HasInput(index)) return null;
        OnnxPlanSlot slot = _slots[_inputSlots[index]];
        return slot.Kind == OnnxSlotKind.Initializer ? slot.Initializer : null;
    }

    /// <summary>
    /// Takes a weight into the kernel's own state, so the run-time plan stops feeding that input and the
    /// original layout can be released when nothing else wants it.
    /// </summary>
    /// <param name="index">The input's position.</param>
    internal void FoldInput(int index)
    {
        int slot = _inputSlots[index];
        _inputSlots[index] = -1;
        if (--_initializerReaders[slot] > 0) return;

        OnnxPlanSlot weight = _slots[slot];
        if (weight.Kind != OnnxSlotKind.Initializer || weight.IsGraphOutput) return;

        // Every node that wanted this weight has taken it, each in its own layout. The stored one is let go
        // HERE rather than at the end of the load, so that the model never holds two copies of the whole of
        // its weights at once.
        ReleasedBytes += OnnxLoadReclaim.Bytes(weight.Initializer.ElementType, weight.Initializer.Count);
        weight.Initializer = null;
        weight.FoldedIntoKernels = true;
    }

    /// <summary>Names the node for a message.</summary>
    /// <returns>A short description, for example <c>MatMul node '/model/layers.0/MatMul'</c>.</returns>
    internal string Describe() => string.IsNullOrEmpty(NodeName)
        ? OpType + " node " + Index.ToString(CultureInfo.InvariantCulture)
        : OpType + " node '" + NodeName + "'";

    /// <summary>Builds the exception for something the engine will not load.</summary>
    /// <param name="reason">What is wrong, as a sentence fragment.</param>
    /// <returns>The exception, for the caller to throw.</returns>
    internal ModelLoadException Refuse(string reason) =>
        new ModelLoadException(Describe() + ": " + reason + ".");

    private static string Range(int least, int most)
    {
        if (least == most) return least.ToString(CultureInfo.InvariantCulture) + ".";
        return "between " + least.ToString(CultureInfo.InvariantCulture) + " and "
            + most.ToString(CultureInfo.InvariantCulture) + ".";
    }
}

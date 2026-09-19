namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A loaded graph made ready to run: its slot table, its nodes in the order they execute, which slots the
/// caller fills and which ones it gets back, and what the file said about itself.
/// </summary>
/// <remarks>
/// The plan is worked out once, when the model is loaded, and never again. What it does NOT hold is a shape:
/// every tensor's shape falls out of the tensors a run is given, so the same plan runs a prompt of five
/// hundred positions and a cached step of one without being rebuilt.
/// </remarks>
internal sealed class OnnxExecutionPlan
{
    /// <summary>Creates a plan.</summary>
    /// <param name="slots">Every named tensor in the graph.</param>
    /// <param name="nodes">The nodes, in the order they execute.</param>
    /// <param name="inputSlots">The slots the caller fills, in the order the graph declares them.</param>
    /// <param name="outputSlots">The slots the caller gets back, in the order the graph declares them.</param>
    /// <param name="metadata">What the file says about itself.</param>
    internal OnnxExecutionPlan(
        OnnxPlanSlot[] slots,
        OnnxPlanNode[] nodes,
        int[] inputSlots,
        int[] outputSlots,
        OnnxModelMetadata metadata)
    {
        Slots = slots;
        Nodes = nodes;
        InputSlots = inputSlots;
        OutputSlots = outputSlots;
        Metadata = metadata;
    }

    /// <summary>Every named tensor in the graph.</summary>
    internal OnnxPlanSlot[] Slots { get; }

    /// <summary>The nodes, in the order they execute.</summary>
    internal OnnxPlanNode[] Nodes { get; }

    /// <summary>The slots the caller fills, in the order the graph declares them.</summary>
    internal int[] InputSlots { get; }

    /// <summary>The slots the caller gets back, in the order the graph declares them.</summary>
    internal int[] OutputSlots { get; }

    /// <summary>What the file says about itself.</summary>
    internal OnnxModelMetadata Metadata { get; }
}

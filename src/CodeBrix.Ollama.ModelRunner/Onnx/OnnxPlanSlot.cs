namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One named tensor in an execution plan: where its value comes from and which node last reads it.
/// </summary>
internal sealed class OnnxPlanSlot
{
    /// <summary>Creates a slot.</summary>
    /// <param name="index">Its position in the plan's slot table.</param>
    /// <param name="name">The name the graph gives the tensor.</param>
    internal OnnxPlanSlot(int index, string name)
    {
        Index = index;
        Name = name;
        LastUse = -1;
        Producer = -1;
    }

    /// <summary>Its position in the plan's slot table.</summary>
    internal int Index { get; }

    /// <summary>The name the graph gives the tensor.</summary>
    internal string Name { get; }

    /// <summary>Where the value comes from.</summary>
    internal OnnxSlotKind Kind { get; set; }

    /// <summary>The weight, for a slot of kind <see cref="OnnxSlotKind.Initializer"/>.</summary>
    internal OnnxValue Initializer { get; set; }

    /// <summary>The index of the node that computes it, or -1 when no node does.</summary>
    internal int Producer { get; set; }

    /// <summary>The index of the last node that reads it, or -1 when no node reads it.</summary>
    internal int LastUse { get; set; }

    /// <summary>Whether the graph hands this tensor back to the caller.</summary>
    internal bool IsGraphOutput { get; set; }

    /// <summary>Whether every node that named this weight folded it into its own state at load time.</summary>
    internal bool FoldedIntoKernels { get; set; }
}

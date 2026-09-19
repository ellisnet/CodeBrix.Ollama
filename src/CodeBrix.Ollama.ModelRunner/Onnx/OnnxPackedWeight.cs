namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A constant matrix-multiply weight turned round at load time: the graph stores it as [K, N] and this holds
/// it as [N, K], so every output element is one contiguous dot product.
/// </summary>
/// <remarks>
/// The reason is threading rather than arithmetic. In the stored layout a thread sweeps whole rows of the
/// weight and accumulates across the whole width of the result, so two threads write into the same cache
/// lines; turned round, each thread owns whole output elements and touches nothing another thread is writing.
/// Measured on a decoder's own shapes, that is worth several times the throughput once more than one thread
/// is at work, and it costs one pass over the weight when the model is loaded.
/// </remarks>
internal sealed class OnnxPackedWeight
{
    /// <summary>Creates a packed weight.</summary>
    /// <param name="values">The elements, in [N, K] order.</param>
    /// <param name="reduction">The length of the reduction, which the graph calls K.</param>
    /// <param name="width">The width of the result, which the graph calls N.</param>
    internal OnnxPackedWeight(float[] values, int reduction, int width)
    {
        Values = values;
        Reduction = reduction;
        Width = width;
    }

    /// <summary>The elements, in [N, K] order.</summary>
    internal float[] Values { get; }

    /// <summary>The length of the reduction, which the graph calls K.</summary>
    internal int Reduction { get; }

    /// <summary>The width of the result, which the graph calls N.</summary>
    internal int Width { get; }
}

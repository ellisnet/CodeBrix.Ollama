namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A constant 8-bit weight of a <c>MatMulInteger</c> node, turned round at load time to [N, K] and held one
/// byte per element, with the zero points its columns are measured from.
/// </summary>
/// <remarks>
/// An unsigned weight is stored as signed bytes with its zero points shifted down by 128, which is the same
/// arithmetic and leaves one kernel to write instead of four. <see cref="ZeroPoints"/> holds either one value
/// for the whole weight or one per column.
/// </remarks>
internal sealed class OnnxIntegerWeight
{
    /// <summary>Creates the weight.</summary>
    /// <param name="values">The elements in [n, k] order, as signed bytes.</param>
    /// <param name="zeroPoints">One zero point, or one per column.</param>
    /// <param name="reduction">The length of the reduction, which the graph calls K.</param>
    /// <param name="width">The number of weight rows, which is the width of the result.</param>
    internal OnnxIntegerWeight(sbyte[] values, int[] zeroPoints, int reduction, int width)
    {
        Values = values;
        ZeroPoints = zeroPoints;
        Reduction = reduction;
        Width = width;
    }

    /// <summary>The elements in [n, k] order, as signed bytes.</summary>
    internal sbyte[] Values { get; }

    /// <summary>One zero point, or one per column.</summary>
    internal int[] ZeroPoints { get; }

    /// <summary>The length of the reduction, which the graph calls K.</summary>
    internal int Reduction { get; }

    /// <summary>The number of weight rows, which is the width of the result.</summary>
    internal int Width { get; }
}

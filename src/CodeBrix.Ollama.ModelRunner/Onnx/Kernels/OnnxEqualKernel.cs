namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Equal</c>: whether each element of the first tensor is the same as the matching element of the second.
/// </summary>
/// <remarks>
/// It answers for booleans as well as for numbers, because a graph that builds an attention mask compares two
/// masks as often as it compares two numbers. A comparison against a floating-point NaN is false, which is
/// what IEEE 754 says.
/// </remarks>
internal sealed class OnnxEqualKernel : OnnxKernel,
    IOnnxCompareOperation<float>,
    IOnnxCompareOperation<long>,
    IOnnxCompareOperation<int>,
    IOnnxCompareOperation<bool>
{
    /// <inheritdoc />
    internal override string OpType => "Equal";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <summary>Compares two floats.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>Whether they are the same.</returns>
    public static bool Apply(float left, float right) => left == right;

    /// <summary>Compares two 64-bit integers.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>Whether they are the same.</returns>
    public static bool Apply(long left, long right) => left == right;

    /// <summary>Compares two 32-bit integers.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>Whether they are the same.</returns>
    public static bool Apply(int left, int right) => left == right;

    /// <summary>Compares two booleans.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>Whether they are the same.</returns>
    public static bool Apply(bool left, bool right) => left == right;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue left = context.RequireInput(0);
        OnnxValue right = context.RequireInput(1);
        if (left.ElementType == OnnxElementType.Bool && right.ElementType == OnnxElementType.Bool)
        {
            OnnxElementwise.CompareTyped<OnnxEqualKernel, bool>(context, left, right);
            return;
        }

        OnnxElementwise.Compare<OnnxEqualKernel>(context);
    }
}

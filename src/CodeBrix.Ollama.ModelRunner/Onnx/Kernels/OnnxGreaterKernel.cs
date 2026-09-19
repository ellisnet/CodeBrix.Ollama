namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Greater</c>: whether each element of the first tensor is above the matching element of the second.
/// </summary>
/// <remarks>
/// The two tensors are broadcast against each other and the result is a boolean tensor, whatever the elements
/// were. A comparison against a floating-point NaN is false, which is what IEEE 754 says and what C# does.
/// </remarks>
internal sealed class OnnxGreaterKernel : OnnxKernel,
    IOnnxCompareOperation<float>,
    IOnnxCompareOperation<long>,
    IOnnxCompareOperation<int>
{
    /// <inheritdoc />
    internal override string OpType => "Greater";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <summary>Compares two floats.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>Whether the left is above the right.</returns>
    public static bool Apply(float left, float right) => left > right;

    /// <summary>Compares two 64-bit integers.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>Whether the left is above the right.</returns>
    public static bool Apply(long left, long right) => left > right;

    /// <summary>Compares two 32-bit integers.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>Whether the left is above the right.</returns>
    public static bool Apply(int left, int right) => left > right;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context) => OnnxElementwise.Compare<OnnxGreaterKernel>(context);
}

using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Add</c>: adds two tensors element by element.
/// </summary>
/// <remarks>
/// The two tensors are broadcast against each other by numpy's bidirectional rules, and both carry the same
/// element type - float, int64 or int32 - which is the type of the result.
/// </remarks>
internal sealed class OnnxAddKernel : OnnxKernel,
    IOnnxBinaryOperation<float>,
    IOnnxBinaryOperation<long>,
    IOnnxBinaryOperation<int>,
    IOnnxVectorOperation
{
    /// <inheritdoc />
    internal override string OpType => "Add";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <summary>Whether the operator has a vector form. It has.</summary>
    public static bool CanVectorize => true;

    /// <summary>Applies the operator to one pair of floats.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>The result.</returns>
    public static float Apply(float left, float right) => left + right;

    /// <summary>Applies the operator to one pair of 64-bit integers.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>The result.</returns>
    public static long Apply(long left, long right) => left + right;

    /// <summary>Applies the operator to one pair of 32-bit integers.</summary>
    /// <param name="left">The left element.</param>
    /// <param name="right">The right element.</param>
    /// <returns>The result.</returns>
    public static int Apply(int left, int right) => left + right;

    /// <summary>Applies the operator to a whole vector of float pairs.</summary>
    /// <param name="left">The left elements.</param>
    /// <param name="right">The right elements.</param>
    /// <returns>The results.</returns>
    public static Vector<float> Apply(Vector<float> left, Vector<float> right) => left + right;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context) => OnnxElementwise.Binary<OnnxAddKernel>(context);
}

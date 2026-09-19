using System;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Softmax</c>: turns each run of numbers along one axis into a distribution that sums to one.
/// </summary>
/// <remarks>
/// <para>
/// This is the opset-13 meaning of the operator and not the older one. Before opset 13 a softmax flattened
/// the tensor into two dimensions at the axis and normalized over everything after it; from 13 it normalizes
/// along THAT AXIS ALONE and leaves every other dimension where it is. The engine refuses a graph written
/// against an older operator set, so there is no question of which is meant here.
/// </para>
/// <para>
/// The largest value of each run is subtracted before the exponential. It cancels out of the quotient
/// exactly, and without it an attention score of a few hundred - which a masked position readily produces -
/// would overflow to infinity and give NaN where the answer is a perfectly ordinary nought.
/// </para>
/// <para>
/// A run that is entirely minus infinity, which is a fully masked row, sums to nought and would divide by it.
/// That is left as the IEEE result rather than papered over, because it says the graph asked for the softmax
/// of nothing.
/// </para>
/// </remarks>
internal sealed class OnnxSoftmaxKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Softmax";

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "axis" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context) => context.Int("axis", -1);

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue input = context.RequireInput(0);
        if (input.ElementType != OnnxElementType.Float)
        {
            throw context.Fail(
                "it normalizes float tensors and was given " + OnnxTensor.Name(input.ElementType) + ".");
        }

        if (input.Rank == 0)
        {
            throw context.Fail("it cannot normalize a scalar.");
        }

        int axis = OnnxShape.NormalizeAxis((long)context.State, input.Rank, context.Node.Describe());
        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Float, (long[])input.Shape.Clone());
        if (result.Count == 0) return;

        int length = (int)input.Shape[axis];
        int inner = 1;
        for (int i = axis + 1; i < input.Rank; i++) inner = checked(inner * (int)input.Shape[i]);
        int outer = input.Count / Math.Max(length * inner, 1);
        if (length == 0) return;

        float[] source = input.Floats;
        float[] target = result.Floats;
        int runs = outer * inner;
        int threads = context.Settings.Threads;

        if (threads > 1 && (long)runs * length >= OnnxGemm.ParallelThreshold)
        {
            int workers = Math.Min(threads, runs);
            Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
            {
                for (int run = worker; run < runs; run += workers)
                {
                    Normalize(source, target, ((run / inner) * length * inner) + (run % inner), length, inner);
                }
            });

            return;
        }

        for (int run = 0; run < runs; run++)
        {
            Normalize(source, target, ((run / inner) * length * inner) + (run % inner), length, inner);
        }
    }

    private static void Normalize(float[] source, float[] target, int start, int length, int stride)
    {
        float largest = float.NegativeInfinity;
        for (int i = 0; i < length; i++)
        {
            float value = source[start + (i * stride)];
            if (value > largest) largest = value;
        }

        float total = 0f;
        for (int i = 0; i < length; i++)
        {
            float value = MathF.Exp(source[start + (i * stride)] - largest);
            target[start + (i * stride)] = value;
            total += value;
        }

        float scale = 1f / total;
        for (int i = 0; i < length; i++)
        {
            target[start + (i * stride)] *= scale;
        }
    }
}

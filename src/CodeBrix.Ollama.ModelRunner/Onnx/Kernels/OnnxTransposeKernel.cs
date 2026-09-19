using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Transpose</c>: the same elements with their axes in another order.
/// </summary>
/// <remarks>
/// With no permutation stated the axes are simply reversed. Attention writes its permutations out -
/// [0,2,1,3] to put the heads in front of the positions, [0,1,3,2] to turn the keys round - and both are
/// ordinary cases of the same walk.
/// </remarks>
internal sealed class OnnxTransposeKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Transpose";

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "perm" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context) => context.Ints("perm");

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue input = context.RequireInput(0);
        long[] stated = (long[])context.State;
        int rank = input.Rank;

        int[] order = new int[rank];
        if (stated.Length == 0)
        {
            for (int i = 0; i < rank; i++) order[i] = rank - 1 - i;
        }
        else
        {
            if (stated.Length != rank)
            {
                throw context.Fail(
                    "its permutation names " + stated.Length.ToString(CultureInfo.InvariantCulture)
                    + " axes and the tensor has " + rank.ToString(CultureInfo.InvariantCulture) + ".");
            }

            bool[] seen = new bool[rank];
            for (int i = 0; i < rank; i++)
            {
                int axis = OnnxShape.NormalizeAxis(stated[i], rank, context.Node.Describe());
                if (seen[axis])
                {
                    throw context.Fail(
                        "its permutation names the axis " + axis.ToString(CultureInfo.InvariantCulture)
                        + " twice.");
                }

                seen[axis] = true;
                order[i] = axis;
            }
        }

        long[] shape = new long[rank];
        int[] own = OnnxShape.Strides(input.Shape);
        int[] strides = new int[rank];
        for (int i = 0; i < rank; i++)
        {
            shape[i] = input.Shape[order[i]];
            strides[i] = own[order[i]];
        }

        OnnxValue result = context.AllocateOutput(0, input.ElementType, shape);
        if (result.Count == 0) return;

        OnnxDataMovement.Strided(input, 0, strides, shape, result);
    }
}

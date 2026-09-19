using System.Globalization;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Reshape</c>: the same elements under another shape.
/// </summary>
/// <remarks>
/// <para>
/// A dimension of -1 is worked out from the rest. A dimension of NOUGHT normally means "keep whatever the
/// input has in that position", which is what <c>allowzero</c> being nought - its default - asks for; setting
/// that attribute makes a nought mean a genuinely empty dimension instead. The difference only shows on a
/// tensor that really is empty, which is exactly what a cached decode step hands in, so it is honoured rather
/// than assumed.
/// </para>
/// <para>
/// Nothing is copied: the result is another description of the same elements.
/// </para>
/// </remarks>
internal sealed class OnnxReshapeKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Reshape";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "allowzero" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context) => context.Int("allowzero", 0) != 0;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        bool allowZero = (bool)context.State;
        OnnxValue data = context.RequireInput(0);
        long[] wanted = OnnxIntegers.Read(context.RequireInput(1), context, "its shape");

        long[] shape = new long[wanted.Length];
        int inferred = -1;
        long known = 1;
        for (int i = 0; i < wanted.Length; i++)
        {
            long dimension = wanted[i];
            if (dimension == -1)
            {
                if (inferred >= 0)
                {
                    throw context.Fail("its shape names more than one dimension to work out.");
                }

                inferred = i;
                continue;
            }

            if (dimension == 0 && !allowZero)
            {
                if (i >= data.Rank)
                {
                    throw context.Fail(
                        "its shape asks to keep dimension " + i.ToString(CultureInfo.InvariantCulture)
                        + " of an input of rank " + data.Rank.ToString(CultureInfo.InvariantCulture) + ".");
                }

                dimension = data.Shape[i];
            }

            if (dimension < 0)
            {
                throw context.Fail(
                    "its shape names a negative dimension, "
                    + dimension.ToString(CultureInfo.InvariantCulture) + ".");
            }

            shape[i] = dimension;
            known = checked(known * dimension);
        }

        if (inferred >= 0)
        {
            if (known == 0)
            {
                throw context.Fail("a dimension cannot be worked out when the others multiply to nought.");
            }

            if (data.Count % known != 0)
            {
                throw context.Fail(
                    "a tensor of " + data.Count.ToString(CultureInfo.InvariantCulture)
                    + " elements does not divide into " + OnnxShape.Describe(shape) + ".");
            }

            shape[inferred] = data.Count / known;
            known *= shape[inferred];
        }

        if (known != data.Count)
        {
            throw context.Fail(
                "a tensor of " + data.Count.ToString(CultureInfo.InvariantCulture)
                + " elements cannot be reshaped to " + OnnxShape.Describe(shape) + ".");
        }

        context.SetOutput(0, data.Reshaped(shape));
    }
}

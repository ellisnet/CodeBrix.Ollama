using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>Trilu</c>: keeps the upper or lower triangle of every matrix in a tensor and ZEROES the rest.
/// </summary>
/// <remarks>
/// <para>
/// It zeroes; it does not write minus infinity. A causal attention mask is built from this in two more steps -
/// compare the triangle against nought, then choose between nought and minus infinity with <c>Where</c> - and
/// an implementation that helpfully wrote minus infinity here would put it in the wrong half of the matrix.
/// </para>
/// <para>
/// With <c>upper</c> set, which is the default, the elements where the column is at least the row plus
/// <c>k</c> are kept; otherwise the elements where it is at most that are kept. A positive <c>k</c> therefore
/// moves the kept triangle away from the diagonal and a negative one moves it across.
/// </para>
/// </remarks>
internal sealed class OnnxTriluKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "Trilu";

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <inheritdoc />
    internal override string[] Attributes => new[] { "upper" };

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context) => context.Int("upper", 1) != 0;

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        bool upper = (bool)context.State;
        OnnxValue input = context.RequireInput(0);
        if (input.Rank < 2)
        {
            throw context.Fail("it needs a tensor of at least rank two.");
        }

        OnnxValue offsetInput = context.Input(1);
        long offset = offsetInput == null ? 0L : OnnxIntegers.ReadScalar(offsetInput, context, "its k");

        OnnxValue result = context.AllocateOutput(0, input.ElementType, (long[])input.Shape.Clone());
        if (result.Count == 0) return;

        Array.Copy(input.Buffer.Data, 0, result.Buffer.Data, 0, result.Count);

        int rows = (int)input.Shape[input.Rank - 2];
        int columns = (int)input.Shape[input.Rank - 1];
        int matrix = rows * columns;
        int matrices = result.Count / matrix;
        Array target = result.Buffer.Data;

        for (int b = 0; b < matrices; b++)
        {
            int at = b * matrix;
            for (int i = 0; i < rows; i++)
            {
                long boundary = i + offset;
                int from;
                int length;
                if (upper)
                {
                    // Everything to the left of column i + k goes.
                    from = 0;
                    length = (int)Math.Clamp(boundary, 0, columns);
                }
                else
                {
                    // Everything to the right of column i + k goes.
                    from = (int)Math.Clamp(boundary + 1, 0, columns);
                    length = columns - from;
                }

                if (length > 0) Array.Clear(target, at + (i * columns) + from, length);
            }
        }
    }
}

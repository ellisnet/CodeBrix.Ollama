using System;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// <c>MatMul</c>: numpy's matrix multiplication, with leading dimensions broadcast and a one-dimensional
/// operand promoted and squeezed back out.
/// </summary>
/// <remarks>
/// <para>
/// Both forms a decoder uses run through here. A layer's weight multiply is [B, S, K] against a CONSTANT
/// [K, N]: that weight is turned round to [N, K] when the model is loaded, the leading dimensions are folded
/// into one long list of rows, and the whole thing is one matrix multiply spread over threads. Attention's
/// own two multiplies are [B, H, S, D] against [B, H, D, T], where the right-hand side is computed and
/// changes every step: those are done batch by batch, in the layout they arrive in, with the batches spread
/// over threads.
/// </para>
/// <para>
/// A reduction of length nought is a real case and gives a result of zeros rather than an error - that is
/// what a first cached step with nothing cached yet asks for.
/// </para>
/// </remarks>
internal sealed class OnnxMatMulKernel : OnnxKernel
{
    /// <inheritdoc />
    internal override string OpType => "MatMul";

    /// <inheritdoc />
    internal override int MinInputs => 2;

    /// <inheritdoc />
    internal override int MaxInputs => 2;

    /// <inheritdoc />
    internal override object Prepare(OnnxNodeLoadContext context)
    {
        OnnxValue weight = context.Initializer(1);
        if (weight == null
            || weight.Rank != 2
            || weight.ElementType != OnnxElementType.Float
            || weight.Count == 0)
        {
            return null;
        }

        int reduction = (int)weight.Shape[0];
        int width = (int)weight.Shape[1];
        float[] packed = new float[weight.Count];
        OnnxGemm.Transpose(weight.Floats, packed, reduction, width);
        context.FoldInput(1);
        return new OnnxPackedWeight(packed, reduction, width);
    }

    /// <inheritdoc />
    internal override void Run(OnnxOperatorContext context)
    {
        OnnxValue left = context.RequireInput(0);
        if (left.ElementType != OnnxElementType.Float)
        {
            throw context.Fail(
                "it multiplies float tensors and its left input is "
                + OnnxTensor.Name(left.ElementType) + ".");
        }

        if (context.State is OnnxPackedWeight packed)
        {
            RunPacked(context, left, packed);
            return;
        }

        RunGeneral(context, left, context.RequireInput(1));
    }

    private static void RunPacked(OnnxOperatorContext context, OnnxValue left, OnnxPackedWeight packed)
    {
        if (left.Rank == 0)
        {
            throw context.Fail("it cannot multiply a scalar.");
        }

        int reduction = (int)left.Shape[left.Rank - 1];
        if (reduction != packed.Reduction)
        {
            throw context.Fail(
                "its left input " + OnnxShape.Describe(left.Shape) + " does not meet a weight of "
                + packed.Reduction.ToString(System.Globalization.CultureInfo.InvariantCulture) + " rows.");
        }

        long[] shape;
        int rows;
        if (left.Rank == 1)
        {
            // numpy promotes a one-dimensional left operand to a single row and takes the row back out again.
            shape = new long[] { packed.Width };
            rows = 1;
        }
        else
        {
            shape = new long[left.Rank];
            rows = 1;
            for (int i = 0; i < left.Rank - 1; i++)
            {
                shape[i] = left.Shape[i];
                rows = checked(rows * (int)left.Shape[i]);
            }

            shape[left.Rank - 1] = packed.Width;
        }

        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Float, shape);
        if (result.Count == 0) return;

        OnnxGemm.MultiplyPacked(
            left.Floats, 0, packed.Values, result.Floats, 0,
            rows, reduction, packed.Width, context.Settings.Kernel, context.Settings.Threads);
    }

    internal static void RunGeneral(
        OnnxOperatorContext context, OnnxValue left, OnnxValue right,
        bool transposeRight = false, float? rightScale = null)
    {
        if (right.ElementType != OnnxElementType.Float)
        {
            throw context.Fail(
                "it multiplies float tensors and its right input is "
                + OnnxTensor.Name(right.ElementType) + ".");
        }

        if (left.Rank == 0 || right.Rank == 0)
        {
            throw context.Fail("it cannot multiply a scalar.");
        }

        bool leftWasVector = left.Rank == 1;
        bool rightWasVector = right.Rank == 1;
        long[] leftShape = leftWasVector ? new long[] { 1, left.Shape[0] } : left.Shape;
        long[] rightShape = rightWasVector ? new long[] { right.Shape[0], 1 } : right.Shape;
        if (transposeRight)
        {
            rightShape = (long[])rightShape.Clone();
            (rightShape[^2], rightShape[^1]) = (rightShape[^1], rightShape[^2]);
        }

        int rows = (int)leftShape[leftShape.Length - 2];
        int reduction = (int)leftShape[leftShape.Length - 1];
        int meeting = (int)rightShape[rightShape.Length - 2];
        int width = (int)rightShape[rightShape.Length - 1];
        if (reduction != meeting)
        {
            throw context.Fail(
                "the shapes " + OnnxShape.Describe(left.Shape) + " and " + OnnxShape.Describe(right.Shape)
                + " do not meet: the reduction is "
                + reduction.ToString(System.Globalization.CultureInfo.InvariantCulture) + " against "
                + meeting.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        long[] leftBatch = Leading(leftShape);
        long[] rightBatch = Leading(rightShape);
        long[] batch = OnnxShape.Broadcast(leftBatch, rightBatch, context.Node.Describe());

        long[] shape = BuildResultShape(batch, rows, width, leftWasVector, rightWasVector);
        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Float, shape);
        if (result.Count == 0) return;

        int batches = OnnxShape.ElementCount(batch);
        int[] leftOffsets = Offsets(leftBatch, batch, batches, rows * reduction);
        int[] rightOffsets = Offsets(rightBatch, batch, batches, reduction * width);
        int stride = rows * width;

        float[] a = left.Floats;
        float[] b = right.Floats;
        float[] c = result.Floats;
        OnnxKernelKind kind = context.Settings.Kernel;
        int threads = context.Settings.Threads;
        long work = (long)batches * rows * width * Math.Max(reduction, 1);

        if (batches > 1 && threads > 1 && work >= OnnxGemm.ParallelThreshold)
        {
            int workers = Math.Min(threads, batches);
            Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
            {
                for (int index = worker; index < batches; index += workers)
                {
                    Multiply(
                        a, leftOffsets[index], b, rightOffsets[index], c, index * stride,
                        rows, reduction, width, kind, 1, transposeRight, rightScale);
                }
            });

            return;
        }

        for (int index = 0; index < batches; index++)
        {
            Multiply(
                a, leftOffsets[index], b, rightOffsets[index], c, index * stride,
                rows, reduction, width, kind, threads, transposeRight, rightScale);
        }
    }

    private static void Multiply(
        float[] a, int aOffset, float[] b, int bOffset, float[] c, int cOffset,
        int rows, int reduction, int width, OnnxKernelKind kind, int threads,
        bool transposeRight, float? rightScale)
    {
        if (transposeRight)
            OnnxTransposedGemm.Multiply(
                a, aOffset, b, bOffset, c, cOffset, rows, reduction, width, kind, threads, rightScale);
        else
            OnnxGemm.Multiply(a, aOffset, b, bOffset, c, cOffset, rows, reduction, width, kind, threads);
    }

    private static long[] Leading(long[] shape)
    {
        long[] leading = new long[shape.Length - 2];
        Array.Copy(shape, leading, leading.Length);
        return leading;
    }

    private static long[] BuildResultShape(
        long[] batch, int rows, int width, bool leftWasVector, bool rightWasVector)
    {
        int extra = (leftWasVector ? 0 : 1) + (rightWasVector ? 0 : 1);
        long[] shape = new long[batch.Length + extra];
        Array.Copy(batch, shape, batch.Length);
        int at = batch.Length;
        if (!leftWasVector) shape[at++] = rows;
        if (!rightWasVector) shape[at] = width;
        return shape;
    }

    private static int[] Offsets(long[] own, long[] batch, int batches, int matrix)
    {
        int[] offsets = new int[batches];
        if (batches == 0) return offsets;

        int rank = batch.Length;
        if (rank == 0) return offsets;

        int[] strides = OnnxShape.BroadcastStrides(own, batch);
        int[] index = new int[rank];
        int running = 0;
        for (int i = 0; i < batches; i++)
        {
            offsets[i] = running * matrix;
            for (int d = rank - 1; d >= 0; d--)
            {
                index[d]++;
                running += strides[d];
                if (index[d] < batch[d]) break;

                running -= strides[d] * (int)batch[d];
                index[d] = 0;
            }
        }

        return offsets;
    }
}

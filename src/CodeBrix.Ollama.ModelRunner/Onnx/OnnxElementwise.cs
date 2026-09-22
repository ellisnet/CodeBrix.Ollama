using System.Numerics;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// numpy's element-by-element rules, written once: bidirectional broadcasting for two tensors, three-way for
/// a selection, and the one-argument walk.
/// </summary>
/// <remarks>
/// <para>
/// Every operator hands itself in as a generic argument, so the arithmetic is compiled into these loops
/// rather than called through them. The walk itself advances an index from the innermost dimension outwards
/// and keeps a running offset into each input, where a dimension a tensor is stretched along has a stride of
/// zero: that is what lets a mask of shape [1,1,S,T] be added to scores of shape [B,H,S,T] without either
/// tensor being expanded first.
/// </para>
/// <para>
/// The innermost dimension is walked in a loop of its own. It is the one that is nearly always contiguous, so
/// the index bookkeeping is paid once per ROW rather than once per element.
/// </para>
/// </remarks>
internal static class OnnxElementwise
{
    /// <summary>Runs a two-argument operator over the node's first two inputs, broadcasting them.</summary>
    /// <typeparam name="TOperation">The operator, which is the kernel class itself.</typeparam>
    /// <param name="context">The node being run.</param>
    internal static void Binary<TOperation>(OnnxOperatorContext context)
        where TOperation : IOnnxBinaryOperation<float>,
            IOnnxBinaryOperation<long>,
            IOnnxBinaryOperation<int>,
            IOnnxVectorOperation
        => Binary<TOperation>(context, context.RequireInput(0), context.RequireInput(1));

    /// <summary>Runs a two-argument operator over two given tensors, broadcasting them.</summary>
    /// <typeparam name="TOperation">The operator, which is the kernel class itself.</typeparam>
    /// <param name="context">The node being run.</param>
    /// <param name="left">The left tensor.</param>
    /// <param name="right">The right tensor.</param>
    internal static void Binary<TOperation>(OnnxOperatorContext context, OnnxValue left, OnnxValue right)
        where TOperation : IOnnxBinaryOperation<float>,
            IOnnxBinaryOperation<long>,
            IOnnxBinaryOperation<int>,
            IOnnxVectorOperation
    {
        if (left.ElementType != right.ElementType)
        {
            throw context.Fail(
                "its two inputs are " + OnnxTensor.Name(left.ElementType) + " and "
                + OnnxTensor.Name(right.ElementType) + "; an element-by-element operator takes one type.");
        }

        long[] shape = OnnxShape.Broadcast(left.Shape, right.Shape, context.Node.Describe());
        OnnxValue result = context.AllocateOutput(0, left.ElementType, shape);
        if (result.Count == 0) return;

        switch (left.ElementType)
        {
            case OnnxElementType.Float:
                if (TOperation.CanVectorize
                    && context.Settings.Kernel != OnnxKernelKind.Scalar)
                {
                    if (OnnxShape.SameShape(left.Shape, right.Shape))
                    {
                        BinaryFloatVector<TOperation>(left.Floats, right.Floats, result.Floats, result.Count);
                        return;
                    }

                    if (left.Count == 1 || right.Count == 1)
                    {
                        BinaryFloatScalar<TOperation>(left, right, result);
                        return;
                    }
                }

                BinaryTyped<TOperation, float>(left, right, result, shape);
                return;
            case OnnxElementType.Int64:
                BinaryTyped<TOperation, long>(left, right, result, shape);
                return;
            case OnnxElementType.Int32:
                BinaryTyped<TOperation, int>(left, right, result, shape);
                return;
            default:
                throw context.Fail(
                    "it cannot be applied to " + OnnxTensor.Name(left.ElementType) + " tensors.");
        }
    }

    /// <summary>Runs a two-argument operator over two tensors of one element type, broadcasting them.</summary>
    /// <typeparam name="TOperation">The operator, which is the kernel class itself.</typeparam>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="left">The left tensor.</param>
    /// <param name="right">The right tensor.</param>
    /// <param name="result">The tensor to fill, already of the broadcast shape.</param>
    /// <param name="shape">The broadcast shape.</param>
    internal static void BinaryTyped<TOperation, T>(OnnxValue left, OnnxValue right, OnnxValue result, long[] shape)
        where TOperation : IOnnxBinaryOperation<T>
    {
        T[] a = Elements<T>(left);
        T[] b = Elements<T>(right);
        T[] c = Elements<T>(result);
        int total = result.Count;
        int rank = shape.Length;

        if (rank == 0)
        {
            c[0] = TOperation.Apply(a[0], b[0]);
            return;
        }

        int[] strideA = OnnxShape.BroadcastStrides(left.Shape, shape);
        int[] strideB = OnnxShape.BroadcastStrides(right.Shape, shape);
        int inner = (int)shape[rank - 1];
        int innerA = strideA[rank - 1];
        int innerB = strideB[rank - 1];
        int[] index = new int[rank];
        int offsetA = 0;
        int offsetB = 0;
        int written = 0;

        while (written < total)
        {
            for (int j = 0; j < inner; j++)
            {
                c[written + j] = TOperation.Apply(a[offsetA + (j * innerA)], b[offsetB + (j * innerB)]);
            }

            written += inner;
            Advance(index, shape, rank - 1, strideA, strideB, ref offsetA, ref offsetB);
        }
    }

    /// <summary>Runs a comparison over the node's first two inputs, broadcasting them, into a bool tensor.</summary>
    /// <typeparam name="TOperation">The comparison, which is the kernel class itself.</typeparam>
    /// <param name="context">The node being run.</param>
    internal static void Compare<TOperation>(OnnxOperatorContext context)
        where TOperation : IOnnxCompareOperation<float>,
            IOnnxCompareOperation<long>,
            IOnnxCompareOperation<int>
    {
        OnnxValue left = context.RequireInput(0);
        OnnxValue right = context.RequireInput(1);
        if (left.ElementType != right.ElementType)
        {
            throw context.Fail(
                "its two inputs are " + OnnxTensor.Name(left.ElementType) + " and "
                + OnnxTensor.Name(right.ElementType) + "; a comparison takes one type.");
        }

        switch (left.ElementType)
        {
            case OnnxElementType.Float:
                CompareTyped<TOperation, float>(context, left, right);
                return;
            case OnnxElementType.Int64:
                CompareTyped<TOperation, long>(context, left, right);
                return;
            case OnnxElementType.Int32:
                CompareTyped<TOperation, int>(context, left, right);
                return;
            default:
                throw context.Fail(
                    "it cannot compare " + OnnxTensor.Name(left.ElementType) + " tensors.");
        }
    }

    /// <summary>Runs a comparison over two tensors of one element type, broadcasting them.</summary>
    /// <typeparam name="TOperation">The comparison, which is the kernel class itself.</typeparam>
    /// <typeparam name="T">The element type being compared.</typeparam>
    /// <param name="context">The node being run.</param>
    /// <param name="left">The left tensor.</param>
    /// <param name="right">The right tensor.</param>
    internal static void CompareTyped<TOperation, T>(OnnxOperatorContext context, OnnxValue left, OnnxValue right)
        where TOperation : IOnnxCompareOperation<T>
    {
        long[] shape = OnnxShape.Broadcast(left.Shape, right.Shape, context.Node.Describe());
        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Bool, shape);
        if (result.Count == 0) return;

        T[] a = Elements<T>(left);
        T[] b = Elements<T>(right);
        bool[] c = result.Booleans;
        int total = result.Count;
        int rank = shape.Length;

        if (rank == 0)
        {
            c[0] = TOperation.Apply(a[0], b[0]);
            return;
        }

        int[] strideA = OnnxShape.BroadcastStrides(left.Shape, shape);
        int[] strideB = OnnxShape.BroadcastStrides(right.Shape, shape);
        int inner = (int)shape[rank - 1];
        int innerA = strideA[rank - 1];
        int innerB = strideB[rank - 1];
        int[] index = new int[rank];
        int offsetA = 0;
        int offsetB = 0;
        int written = 0;

        while (written < total)
        {
            for (int j = 0; j < inner; j++)
            {
                c[written + j] = TOperation.Apply(a[offsetA + (j * innerA)], b[offsetB + (j * innerB)]);
            }

            written += inner;
            Advance(index, shape, rank - 1, strideA, strideB, ref offsetA, ref offsetB);
        }
    }

    /// <summary>Runs a one-argument float operator over the node's only input.</summary>
    /// <typeparam name="TOperation">The operator, which is the kernel class itself.</typeparam>
    /// <param name="context">The node being run.</param>
    internal static void UnaryFloat<TOperation>(OnnxOperatorContext context)
        where TOperation : IOnnxUnaryOperation<float>, IOnnxUnaryVectorOperation
    {
        OnnxValue input = context.RequireInput(0);
        if (input.ElementType != OnnxElementType.Float)
        {
            throw context.Fail(
                "it takes a float tensor and was given " + OnnxTensor.Name(input.ElementType) + ".");
        }

        OnnxValue result = context.AllocateOutput(0, OnnxElementType.Float, (long[])input.Shape.Clone());
        int total = result.Count;
        if (total == 0) return;

        float[] source = input.Floats;
        float[] target = result.Floats;
        int i = 0;

        if (TOperation.CanVectorize && context.Settings.Kernel != OnnxKernelKind.Scalar)
        {
            int width = Vector<float>.Count;
            for (; i + width <= total; i += width)
            {
                TOperation.Apply(new Vector<float>(source, i)).CopyTo(target, i);
            }
        }

        for (; i < total; i++)
        {
            target[i] = TOperation.Apply(source[i]);
        }
    }

    /// <summary>Runs a one-argument operator over the node's only input, for one element type.</summary>
    /// <typeparam name="TOperation">The operator, which is the kernel class itself.</typeparam>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="context">The node being run.</param>
    /// <param name="input">The tensor to read.</param>
    internal static void UnaryTyped<TOperation, T>(OnnxOperatorContext context, OnnxValue input)
        where TOperation : IOnnxUnaryOperation<T>
    {
        OnnxValue result = context.AllocateOutput(0, input.ElementType, (long[])input.Shape.Clone());
        T[] source = Elements<T>(input);
        T[] target = Elements<T>(result);
        for (int i = 0; i < result.Count; i++)
        {
            target[i] = TOperation.Apply(source[i]);
        }
    }

    /// <summary>
    /// Selects element by element from two tensors under a condition, broadcasting all three against each
    /// other, which is what <c>Where</c> means.
    /// </summary>
    /// <typeparam name="T">The element type of the two branches.</typeparam>
    /// <param name="condition">The condition tensor.</param>
    /// <param name="whenTrue">The tensor read where the condition holds.</param>
    /// <param name="whenFalse">The tensor read where it does not.</param>
    /// <param name="result">The tensor to fill, already of the three-way broadcast shape.</param>
    /// <param name="shape">The three-way broadcast shape.</param>
    internal static void Select<T>(
        OnnxValue condition, OnnxValue whenTrue, OnnxValue whenFalse, OnnxValue result, long[] shape)
    {
        bool[] test = condition.Booleans;
        T[] a = Elements<T>(whenTrue);
        T[] b = Elements<T>(whenFalse);
        T[] c = Elements<T>(result);
        int total = result.Count;
        int rank = shape.Length;

        if (rank == 0)
        {
            c[0] = test[0] ? a[0] : b[0];
            return;
        }

        int[] strideC = OnnxShape.BroadcastStrides(condition.Shape, shape);
        int[] strideA = OnnxShape.BroadcastStrides(whenTrue.Shape, shape);
        int[] strideB = OnnxShape.BroadcastStrides(whenFalse.Shape, shape);
        int inner = (int)shape[rank - 1];
        int innerC = strideC[rank - 1];
        int innerA = strideA[rank - 1];
        int innerB = strideB[rank - 1];
        int[] index = new int[rank];
        int offsetC = 0;
        int offsetA = 0;
        int offsetB = 0;
        int written = 0;

        while (written < total)
        {
            for (int j = 0; j < inner; j++)
            {
                c[written + j] = test[offsetC + (j * innerC)]
                    ? a[offsetA + (j * innerA)]
                    : b[offsetB + (j * innerB)];
            }

            written += inner;

            for (int d = rank - 2; d >= 0; d--)
            {
                index[d]++;
                offsetC += strideC[d];
                offsetA += strideA[d];
                offsetB += strideB[d];
                if (index[d] < shape[d]) break;

                offsetC -= strideC[d] * (int)shape[d];
                offsetA -= strideA[d] * (int)shape[d];
                offsetB -= strideB[d] * (int)shape[d];
                index[d] = 0;
            }
        }
    }

    /// <summary>The elements of a tensor, as the array of the type it carries.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="value">The tensor.</param>
    /// <returns>Its array.</returns>
    internal static T[] Elements<T>(OnnxValue value) => (T[])value.Buffer.Data;

    private static void BinaryFloatVector<TOperation>(float[] a, float[] b, float[] c, int total)
        where TOperation : IOnnxBinaryOperation<float>, IOnnxVectorOperation
    {
        int width = Vector<float>.Count;
        int i = 0;
        for (; i + width <= total; i += width)
        {
            TOperation.Apply(new Vector<float>(a, i), new Vector<float>(b, i)).CopyTo(c, i);
        }

        // The tail runs through the operator's scalar form. A vector lane and the scalar do the same IEEE
        // arithmetic on the same pair of numbers, so the two halves of the loop cannot drift apart.
        for (; i < total; i++)
        {
            c[i] = TOperation.Apply(a[i], b[i]);
        }
    }

    private static void BinaryFloatScalar<TOperation>(OnnxValue left, OnnxValue right, OnnxValue result)
        where TOperation : IOnnxBinaryOperation<float>, IOnnxVectorOperation
    {
        bool scalarLeft = left.Count == 1;
        float scalar = scalarLeft ? left.Floats[0] : right.Floats[0];
        float[] source = scalarLeft ? right.Floats : left.Floats;
        float[] target = result.Floats;
        Vector<float> broadcast = new Vector<float>(scalar);
        int width = Vector<float>.Count;
        int i = 0;
        for (; i + width <= result.Count; i += width)
        {
            Vector<float> values = new Vector<float>(source, i);
            (scalarLeft ? TOperation.Apply(broadcast, values) : TOperation.Apply(values, broadcast))
                .CopyTo(target, i);
        }

        for (; i < result.Count; i++)
        {
            target[i] = scalarLeft ? TOperation.Apply(scalar, source[i]) : TOperation.Apply(source[i], scalar);
        }
    }

    private static void Advance(
        int[] index, long[] shape, int from, int[] strideA, int[] strideB, ref int offsetA, ref int offsetB)
    {
        for (int d = from - 1; d >= 0; d--)
        {
            index[d]++;
            offsetA += strideA[d];
            offsetB += strideB[d];
            if (index[d] < shape[d]) return;

            offsetA -= strideA[d] * (int)shape[d];
            offsetB -= strideB[d] * (int)shape[d];
            index[d] = 0;
        }
    }
}

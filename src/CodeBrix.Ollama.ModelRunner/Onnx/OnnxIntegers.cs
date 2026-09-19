namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Reading the small integer tensors a graph does its shape arithmetic with.
/// </summary>
/// <remarks>
/// Shapes, axes, indices and ranges are ordinary tensors here: a graph works out how long its cache is by
/// running <c>Shape</c>, <c>Gather</c> and <c>Concat</c> on int64 tensors, and the result is fed to
/// <c>Reshape</c> or <c>Slice</c> like any other input. Everything the specification writes as int64 is
/// accepted as int32 as well, because an exporter occasionally narrows one.
/// </remarks>
internal static class OnnxIntegers
{
    /// <summary>Reads a small integer tensor into an array.</summary>
    /// <param name="value">The tensor.</param>
    /// <param name="context">The node being run, for the message when the tensor is the wrong type.</param>
    /// <param name="what">What the tensor is, for that message.</param>
    /// <returns>Its elements.</returns>
    /// <exception cref="InferenceException">The tensor is not an integer one.</exception>
    internal static long[] Read(OnnxValue value, OnnxOperatorContext context, string what)
    {
        long[] values = new long[value.Count];
        switch (value.ElementType)
        {
            case OnnxElementType.Int64:
            {
                long[] source = value.Int64s;
                for (int i = 0; i < values.Length; i++) values[i] = source[i];
                return values;
            }

            case OnnxElementType.Int32:
            {
                int[] source = value.Int32s;
                for (int i = 0; i < values.Length; i++) values[i] = source[i];
                return values;
            }

            default:
                throw context.Fail(
                    what + " is a " + OnnxTensor.Name(value.ElementType) + " tensor and must be an integer one.");
        }
    }

    /// <summary>Reads a single-element integer tensor.</summary>
    /// <param name="value">The tensor, which must hold exactly one element.</param>
    /// <param name="context">The node being run, for the message when it does not.</param>
    /// <param name="what">What the tensor is, for that message.</param>
    /// <returns>Its one element.</returns>
    /// <exception cref="InferenceException">The tensor is not an integer one, or does not hold exactly one element.</exception>
    internal static long ReadScalar(OnnxValue value, OnnxOperatorContext context, string what)
    {
        if (value.Count != 1)
        {
            throw context.Fail(
                what + " holds " + value.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " elements and must hold one.");
        }

        return Read(value, context, what)[0];
    }
}

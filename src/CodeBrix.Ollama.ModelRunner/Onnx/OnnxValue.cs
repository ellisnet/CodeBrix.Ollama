using System;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One tensor while a graph is running: an element type, a shape, and the buffer holding its elements in
/// row-major order.
/// </summary>
/// <remarks>
/// The buffer may be longer than the shape asks for - it came from the arena and the arena hands out what
/// fits - so every kernel works to <see cref="Count"/> and never to the array's own length.
/// </remarks>
internal sealed class OnnxValue
{
    private OnnxValue(OnnxElementType elementType, long[] shape, int count, OnnxBuffer buffer)
    {
        ElementType = elementType;
        Shape = shape;
        Count = count;
        Buffer = buffer;
        buffer.Retain();
    }

    /// <summary>The type of every element.</summary>
    internal OnnxElementType ElementType { get; }

    /// <summary>The shape, outermost dimension first. An empty array is a scalar.</summary>
    internal long[] Shape { get; }

    /// <summary>The number of dimensions.</summary>
    internal int Rank => Shape.Length;

    /// <summary>The number of elements, which is the product of the shape and is 1 for a scalar.</summary>
    internal int Count { get; }

    /// <summary>The buffer the elements live in.</summary>
    internal OnnxBuffer Buffer { get; }

    /// <summary>The elements of a float tensor.</summary>
    internal float[] Floats => (float[])Buffer.Data;

    /// <summary>The elements of an int64 tensor.</summary>
    internal long[] Int64s => (long[])Buffer.Data;

    /// <summary>The elements of an int32 tensor.</summary>
    internal int[] Int32s => (int[])Buffer.Data;

    /// <summary>The elements of a bool tensor.</summary>
    internal bool[] Booleans => (bool[])Buffer.Data;

    /// <summary>The elements of an unsigned 8-bit tensor - a quantized activation or weight.</summary>
    internal byte[] Bytes => (byte[])Buffer.Data;

    /// <summary>The elements of a signed 8-bit tensor - a quantized weight.</summary>
    internal sbyte[] SignedBytes => (sbyte[])Buffer.Data;

    /// <summary>Allocates a tensor of the given shape out of an arena.</summary>
    /// <param name="arena">The arena to take the buffer from.</param>
    /// <param name="elementType">The element type.</param>
    /// <param name="shape">The shape, which the caller owns from here on.</param>
    /// <param name="poolable">Whether the buffer may go back to the arena; false for a run's own outputs.</param>
    /// <returns>The tensor.</returns>
    internal static OnnxValue Allocate(OnnxArena arena, OnnxElementType elementType, long[] shape, bool poolable)
    {
        int count = OnnxShape.ElementCount(shape);
        return new OnnxValue(elementType, shape, count, arena.Rent(elementType, count, poolable));
    }

    /// <summary>Describes the same elements under a different shape, without copying them.</summary>
    /// <param name="shape">The new shape, whose element count must match this tensor's.</param>
    /// <returns>The tensor.</returns>
    internal OnnxValue Reshaped(long[] shape) => new OnnxValue(ElementType, shape, Count, Buffer);

    /// <summary>Wraps an array the engine did not allocate - a caller's input tensor - without copying it.</summary>
    /// <param name="elementType">The element type.</param>
    /// <param name="data">The array.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The tensor.</returns>
    internal static OnnxValue Wrap(OnnxElementType elementType, Array data, long[] shape) =>
        new OnnxValue(elementType, shape, OnnxShape.ElementCount(shape), new OnnxBuffer(elementType, data, false, false));

    /// <summary>Lets go of the tensor, returning its buffer to the arena when nothing else reads it.</summary>
    /// <param name="arena">The arena the buffer came from.</param>
    internal void Release(OnnxArena arena)
    {
        if (Buffer.Release()) arena.Return(Buffer);
    }

    /// <summary>Returns the element type and the shape.</summary>
    /// <returns>A one-line description.</returns>
    public override string ToString()
    {
        StringBuilder text = new StringBuilder();
        text.Append(OnnxTensor.Name(ElementType));
        text.Append('[');
        for (int i = 0; i < Shape.Length; i++)
        {
            if (i > 0) text.Append(',');
            text.Append(Shape[i].ToString(CultureInfo.InvariantCulture));
        }

        text.Append(']');
        return text.ToString();
    }
}

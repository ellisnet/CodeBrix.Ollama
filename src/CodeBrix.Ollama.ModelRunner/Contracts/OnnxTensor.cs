using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// A dense tensor handed to, or handed back by, <see cref="IOnnxModel.Run"/>: an element type, a shape, and
/// one array holding the elements in row-major order.
/// </summary>
/// <remarks>
/// <para>
/// THE ARRAY IS NOT COPIED, in either direction. A tensor built with one of the <c>From</c> methods keeps the
/// array it was handed, and the engine reads it where it lies; a tensor that comes out of a run owns an array
/// of exactly <see cref="Count"/> elements that nothing else will write to. That is what lets a driver take a
/// <c>present</c> output of one step and pass the very same instance back as the <c>past</c> input of the
/// next one without copying a cache that can run to hundreds of megabytes.
/// </para>
/// <para>
/// The consequence is the usual one: an array handed in must not be written to while a run is in flight, and
/// a tensor's array must not be modified while another run still reads it as an input.
/// </para>
/// <para>
/// A dimension of zero is ordinary and not an error - an empty tensor is how a cached decode step says it has
/// no new positions to add - and every operator handles it.
/// </para>
/// </remarks>
public sealed class OnnxTensor
{
    private readonly Array _data;

    private OnnxTensor(OnnxElementType elementType, Array data, long[] shape)
    {
        ElementType = elementType;
        _data = data;
        Shape = shape;
        Count = ElementCount(shape);
    }

    /// <summary>The type of every element.</summary>
    public OnnxElementType ElementType { get; }

    /// <summary>The shape, outermost dimension first. A rank of zero is a scalar.</summary>
    public IReadOnlyList<long> Shape { get; }

    /// <summary>The number of elements, which is the product of <see cref="Shape"/> and is 1 for a scalar.</summary>
    public long Count { get; }

    /// <summary>
    /// The elements of a <see cref="OnnxElementType.Float"/> tensor, in row-major order, or
    /// <see langword="null"/> when the tensor carries another type. The array is the tensor's own and is not
    /// copied.
    /// </summary>
    public float[] Floats => _data as float[];

    /// <summary>
    /// The elements of an <see cref="OnnxElementType.Int32"/> tensor, in row-major order, or
    /// <see langword="null"/> when the tensor carries another type. The array is the tensor's own and is not
    /// copied.
    /// </summary>
    public int[] Int32s => _data as int[];

    /// <summary>
    /// The elements of an <see cref="OnnxElementType.Int64"/> tensor, in row-major order, or
    /// <see langword="null"/> when the tensor carries another type. The array is the tensor's own and is not
    /// copied.
    /// </summary>
    public long[] Int64s => _data as long[];

    /// <summary>
    /// The elements of an <see cref="OnnxElementType.Bool"/> tensor, in row-major order, or
    /// <see langword="null"/> when the tensor carries another type. The array is the tensor's own and is not
    /// copied.
    /// </summary>
    public bool[] Booleans => _data as bool[];

    /// <summary>Wraps a 32-bit float array as a tensor of the given shape, without copying it.</summary>
    /// <param name="values">The elements in row-major order. It must hold at least as many as the shape asks for.</param>
    /// <param name="shape">The shape, outermost dimension first; an empty shape makes a scalar.</param>
    /// <returns>The tensor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="shape"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A dimension is negative, the element count does not fit in an array, or the array is too short.</exception>
    public static OnnxTensor FromFloats(float[] values, params long[] shape) =>
        Create(OnnxElementType.Float, values, shape);

    /// <summary>Wraps a 32-bit integer array as a tensor of the given shape, without copying it.</summary>
    /// <param name="values">The elements in row-major order. It must hold at least as many as the shape asks for.</param>
    /// <param name="shape">The shape, outermost dimension first; an empty shape makes a scalar.</param>
    /// <returns>The tensor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="shape"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A dimension is negative, the element count does not fit in an array, or the array is too short.</exception>
    public static OnnxTensor FromInt32(int[] values, params long[] shape) =>
        Create(OnnxElementType.Int32, values, shape);

    /// <summary>Wraps a 64-bit integer array as a tensor of the given shape, without copying it.</summary>
    /// <param name="values">The elements in row-major order. It must hold at least as many as the shape asks for.</param>
    /// <param name="shape">The shape, outermost dimension first; an empty shape makes a scalar.</param>
    /// <returns>The tensor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="shape"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A dimension is negative, the element count does not fit in an array, or the array is too short.</exception>
    public static OnnxTensor FromInt64(long[] values, params long[] shape) =>
        Create(OnnxElementType.Int64, values, shape);

    /// <summary>Wraps a boolean array as a tensor of the given shape, without copying it.</summary>
    /// <param name="values">The elements in row-major order. It must hold at least as many as the shape asks for.</param>
    /// <param name="shape">The shape, outermost dimension first; an empty shape makes a scalar.</param>
    /// <returns>The tensor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or <paramref name="shape"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A dimension is negative, the element count does not fit in an array, or the array is too short.</exception>
    public static OnnxTensor FromBooleans(bool[] values, params long[] shape) =>
        Create(OnnxElementType.Bool, values, shape);

    /// <summary>Returns the element type and the shape, for diagnostics.</summary>
    /// <returns>A one-line description, for example <c>float[1,4,8,256]</c>.</returns>
    public override string ToString()
    {
        StringBuilder text = new StringBuilder();
        text.Append(Name(ElementType));
        text.Append('[');
        for (int i = 0; i < Shape.Count; i++)
        {
            if (i > 0) text.Append(',');
            text.Append(Shape[i].ToString(CultureInfo.InvariantCulture));
        }

        text.Append(']');
        return text.ToString();
    }

    /// <summary>The spelling of an element type used in messages.</summary>
    /// <param name="elementType">The type.</param>
    /// <returns>Its name.</returns>
    internal static string Name(OnnxElementType elementType) => elementType switch
    {
        OnnxElementType.Float => "float",
        OnnxElementType.UInt8 => "uint8",
        OnnxElementType.Int8 => "int8",
        OnnxElementType.Int32 => "int32",
        OnnxElementType.Int64 => "int64",
        OnnxElementType.Bool => "bool",
        _ => ((int)elementType).ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Builds a tensor over an array the engine itself allocated at exactly the right length.</summary>
    /// <param name="elementType">The element type the array carries.</param>
    /// <param name="data">The array.</param>
    /// <param name="shape">The shape, which the caller has already validated.</param>
    /// <returns>The tensor.</returns>
    internal static OnnxTensor Adopt(OnnxElementType elementType, Array data, long[] shape) =>
        new OnnxTensor(elementType, data, shape);

    /// <summary>The tensor's array, whatever its element type.</summary>
    /// <returns>The array.</returns>
    internal Array Data() => _data;

    private static OnnxTensor Create(OnnxElementType elementType, Array values, long[] shape)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        if (shape == null) throw new ArgumentNullException(nameof(shape));

        long[] copy = new long[shape.Length];
        for (int i = 0; i < shape.Length; i++)
        {
            if (shape[i] < 0)
            {
                throw new ArgumentException(
                    "A tensor shape cannot hold a negative dimension; dimension "
                    + i.ToString(CultureInfo.InvariantCulture) + " is "
                    + shape[i].ToString(CultureInfo.InvariantCulture) + ".",
                    nameof(shape));
            }

            copy[i] = shape[i];
        }

        long count = ElementCount(copy);
        if (count > int.MaxValue)
        {
            throw new ArgumentException(
                "A tensor of " + count.ToString(CultureInfo.InvariantCulture)
                + " elements is larger than a .NET array can be.",
                nameof(shape));
        }

        if (values.Length < count)
        {
            throw new ArgumentException(
                "The shape asks for " + count.ToString(CultureInfo.InvariantCulture)
                + " elements and the array holds " + values.Length.ToString(CultureInfo.InvariantCulture) + ".",
                nameof(values));
        }

        return new OnnxTensor(elementType, values, copy);
    }

    private static long ElementCount(IReadOnlyList<long> shape)
    {
        long count = 1;
        for (int i = 0; i < shape.Count; i++)
        {
            count *= shape[i];
            if (count == 0) return 0;
        }

        return count;
    }
}

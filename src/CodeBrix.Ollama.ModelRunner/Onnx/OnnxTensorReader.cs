using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Turns a tensor as the file stores it into a tensor the engine computes with.
/// </summary>
/// <remarks>
/// <para>
/// The bytes arrive either in the message's raw-data field or in one of its typed lists, and the engine has
/// to read both - the same file uses one for a weight matrix and the other for a two-element shape. Raw data
/// is reinterpreted rather than decoded element by element, which is what makes an 822 MB model load in
/// hundreds of milliseconds rather than tens of seconds.
/// </para>
/// <para>
/// 16-bit floats are widened here, once, because the engine computes in 32-bit floats throughout; that is a
/// storage format rather than a compute format. Every other element type the specification allows is refused
/// by name.
/// </para>
/// </remarks>
internal static class OnnxTensorReader
{
    /// <summary>Reads one stored tensor, whose raw data has already been resolved if it lived in a side file.</summary>
    /// <param name="tensor">The stored tensor.</param>
    /// <param name="what">What the tensor is, for the message when it cannot be read.</param>
    /// <returns>The tensor, over a buffer of its own that no arena will reuse.</returns>
    /// <exception cref="ModelLoadException">The element type is one the engine does not compute in, or the payload is the wrong length.</exception>
    internal static OnnxValue Read(OnnxTensorProto tensor, string what)
    {
        long[] shape = tensor.Dimensions.ToArray();
        long total = 1;
        for (int i = 0; i < shape.Length; i++)
        {
            if (shape[i] < 0)
            {
                throw new ModelLoadException(
                    what + " has a negative dimension in its shape " + OnnxShape.Describe(shape) + ".");
            }

            total *= shape[i];
            if (total > int.MaxValue)
            {
                throw new ModelLoadException(
                    what + " of shape " + OnnxShape.Describe(shape)
                    + " holds more elements than a .NET array can.");
            }
        }

        int count = (int)total;
        OnnxTensorDataType type = (OnnxTensorDataType)(tensor.DataType ?? 0);
        byte[] raw = tensor.RawData;

        switch (type)
        {
            case OnnxTensorDataType.Float:
            {
                float[] values = new float[count];
                if (raw != null && raw.Length > 0) CopyRaw<float>(raw, values, count, what, 4);
                else CopyList(tensor.FloatData, values, count, what);
                return Wrap(OnnxElementType.Float, values, shape);
            }

            case OnnxTensorDataType.Float16:
            {
                float[] values = new float[count];
                if (raw != null && raw.Length > 0)
                {
                    Require(raw.Length, count * 2, what);
                    ReadOnlySpan<ushort> bits = MemoryMarshal.Cast<byte, ushort>(raw.AsSpan(0, count * 2));
                    for (int i = 0; i < count; i++) values[i] = (float)BitConverter.UInt16BitsToHalf(bits[i]);
                }
                else
                {
                    // The schema packs a 16-bit float into the low half of an int32 entry.
                    RequireList(tensor.Int32Data.Count, count, what);
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = (float)BitConverter.UInt16BitsToHalf((ushort)tensor.Int32Data[i]);
                    }
                }

                return Wrap(OnnxElementType.Float, values, shape);
            }

            case OnnxTensorDataType.Int64:
            {
                long[] values = new long[count];
                if (raw != null && raw.Length > 0) CopyRaw<long>(raw, values, count, what, 8);
                else CopyList(tensor.Int64Data, values, count, what);
                return Wrap(OnnxElementType.Int64, values, shape);
            }

            case OnnxTensorDataType.Int32:
            {
                int[] values = new int[count];
                if (raw != null && raw.Length > 0) CopyRaw<int>(raw, values, count, what, 4);
                else CopyList(tensor.Int32Data, values, count, what);
                return Wrap(OnnxElementType.Int32, values, shape);
            }

            case OnnxTensorDataType.UInt8:
            {
                // A quantized weight is read AS IT LIES, one byte per element, and it stays that way: the
                // whole point of a four-bit weight is that four bits are what it costs in memory, so nothing
                // here expands it. The kernel that folds it works out what each byte holds.
                byte[] values = new byte[count];
                if (raw != null && raw.Length > 0)
                {
                    Require(raw.Length, count, what);
                    raw.AsSpan(0, count).CopyTo(values);
                }
                else
                {
                    // The schema packs an 8-bit integer into the low byte of an int32 entry.
                    RequireList(tensor.Int32Data.Count, count, what);
                    for (int i = 0; i < count; i++) values[i] = unchecked((byte)tensor.Int32Data[i]);
                }

                return Wrap(OnnxElementType.UInt8, values, shape);
            }

            case OnnxTensorDataType.Int8:
            {
                sbyte[] values = new sbyte[count];
                if (raw != null && raw.Length > 0)
                {
                    Require(raw.Length, count, what);
                    MemoryMarshal.Cast<byte, sbyte>(raw.AsSpan(0, count)).CopyTo(values);
                }
                else
                {
                    RequireList(tensor.Int32Data.Count, count, what);
                    for (int i = 0; i < count; i++) values[i] = unchecked((sbyte)tensor.Int32Data[i]);
                }

                return Wrap(OnnxElementType.Int8, values, shape);
            }

            case OnnxTensorDataType.Bool:
            {
                bool[] values = new bool[count];
                if (raw != null && raw.Length > 0)
                {
                    Require(raw.Length, count, what);
                    for (int i = 0; i < count; i++) values[i] = raw[i] != 0;
                }
                else
                {
                    RequireList(tensor.Int32Data.Count, count, what);
                    for (int i = 0; i < count; i++) values[i] = tensor.Int32Data[i] != 0;
                }

                return Wrap(OnnxElementType.Bool, values, shape);
            }

            default:
                throw new ModelLoadException(
                    what + " is stored as " + Describe(type)
                    + ", and this engine computes in float, int64, int32, bool and the two quantized 8-bit"
                    + " types (a 16-bit float weight is widened, and nothing else is converted).");
        }
    }

    /// <summary>The number of bytes a tensor of this type and element count occupies in raw form.</summary>
    /// <param name="type">The element type.</param>
    /// <param name="count">The number of elements.</param>
    /// <returns>The byte count, or -1 when the type is not one the engine reads.</returns>
    internal static long RawLength(OnnxTensorDataType type, long count) => type switch
    {
        OnnxTensorDataType.Float => count * 4,
        OnnxTensorDataType.Float16 => count * 2,
        OnnxTensorDataType.Int64 => count * 8,
        OnnxTensorDataType.Int32 => count * 4,
        OnnxTensorDataType.Bool => count,
        OnnxTensorDataType.UInt8 => count,
        OnnxTensorDataType.Int8 => count,
        _ => -1,
    };

    /// <summary>Names an element type for a message.</summary>
    /// <param name="type">The element type.</param>
    /// <returns>Its name, or its number when the specification's name is not worth spelling.</returns>
    internal static string Describe(OnnxTensorDataType type) => type switch
    {
        OnnxTensorDataType.Undefined => "no element type at all",
        OnnxTensorDataType.Double => "a 64-bit float",
        OnnxTensorDataType.UInt8 => "an unsigned 8-bit integer",
        OnnxTensorDataType.Int8 => "an 8-bit integer",
        OnnxTensorDataType.UInt16 => "an unsigned 16-bit integer",
        OnnxTensorDataType.Int16 => "a 16-bit integer",
        OnnxTensorDataType.String => "text",
        OnnxTensorDataType.Float16 => "a 16-bit float",
        OnnxTensorDataType.BFloat16 => "a bfloat16",
        OnnxTensorDataType.UInt32 => "an unsigned 32-bit integer",
        OnnxTensorDataType.UInt64 => "an unsigned 64-bit integer",
        _ => "element type " + ((int)type).ToString(CultureInfo.InvariantCulture),
    };

    private static OnnxValue Wrap(OnnxElementType elementType, Array data, long[] shape) =>
        OnnxValue.Wrap(elementType, data, shape);

    private static void CopyRaw<T>(byte[] raw, T[] values, int count, string what, int size)
        where T : struct
    {
        Require(raw.Length, count * (long)size, what);
        MemoryMarshal.Cast<byte, T>(raw.AsSpan(0, count * size)).CopyTo(values);
    }

    private static void CopyList<T>(List<T> source, T[] values, int count, string what)
    {
        RequireList(source.Count, count, what);
        source.CopyTo(0, values, 0, count);
    }

    private static void Require(long actual, long expected, string what)
    {
        if (actual < expected)
        {
            throw new ModelLoadException(
                what + " says it holds " + expected.ToString(CultureInfo.InvariantCulture)
                + " bytes of data and carries " + actual.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }

    private static void RequireList(int actual, int expected, string what)
    {
        if (actual < expected)
        {
            throw new ModelLoadException(
                what + " says it holds " + expected.ToString(CultureInfo.InvariantCulture)
                + " elements and carries " + actual.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }
}

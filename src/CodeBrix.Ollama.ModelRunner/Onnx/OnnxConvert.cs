using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Element-type conversion, as the ONNX <c>Cast</c> operator defines it.
/// </summary>
/// <remarks>
/// <para>
/// A float becoming an integer TRUNCATES towards zero rather than rounding, which is the C conversion the
/// specification names; a value outside the integer's range is left to the processor, which the specification
/// also leaves undefined. Anything becoming a boolean is "not zero", and a boolean becoming a number is one
/// or nought.
/// </para>
/// <para>
/// The two quantized 8-bit types convert like any other integer, and narrowing to one of them keeps the low
/// eight bits rather than saturating - again the C conversion, and again what the specification asks for.
/// </para>
/// </remarks>
internal static class OnnxConvert
{
    /// <summary>Converts every element of one tensor into another tensor's element type.</summary>
    /// <param name="source">The tensor to read.</param>
    /// <param name="target">The tensor to write, of the same element count.</param>
    /// <exception cref="InferenceException">The pair of types is not one the engine converts between.</exception>
    internal static void Convert(OnnxValue source, OnnxValue target)
    {
        int count = source.Count;
        if (source.ElementType == target.ElementType)
        {
            Array.Copy(source.Buffer.Data, 0, target.Buffer.Data, 0, count);
            return;
        }

        switch (source.ElementType)
        {
            case OnnxElementType.Float:
                FromFloat(source.Floats, target, count);
                return;
            case OnnxElementType.Int64:
                FromInt64(source.Int64s, target, count);
                return;
            case OnnxElementType.Int32:
                FromInt32(source.Int32s, target, count);
                return;
            case OnnxElementType.Bool:
                FromBool(source.Booleans, target, count);
                return;
            case OnnxElementType.UInt8:
                FromByte(source.Bytes, target, count);
                return;
            case OnnxElementType.Int8:
                FromSignedByte(source.SignedBytes, target, count);
                return;
            default:
                throw Unsupported(source.ElementType, target.ElementType);
        }
    }

    private static void FromFloat(float[] source, OnnxValue target, int count)
    {
        switch (target.ElementType)
        {
            case OnnxElementType.Int64:
            {
                long[] values = target.Int64s;
                for (int i = 0; i < count; i++) values[i] = (long)source[i];
                return;
            }

            case OnnxElementType.Int32:
            {
                int[] values = target.Int32s;
                for (int i = 0; i < count; i++) values[i] = (int)source[i];
                return;
            }

            case OnnxElementType.Bool:
            {
                bool[] values = target.Booleans;
                for (int i = 0; i < count; i++) values[i] = source[i] != 0f;
                return;
            }

            case OnnxElementType.UInt8:
            {
                byte[] values = target.Bytes;
                for (int i = 0; i < count; i++) values[i] = unchecked((byte)source[i]);
                return;
            }

            case OnnxElementType.Int8:
            {
                sbyte[] values = target.SignedBytes;
                for (int i = 0; i < count; i++) values[i] = unchecked((sbyte)source[i]);
                return;
            }

            default:
                throw Unsupported(OnnxElementType.Float, target.ElementType);
        }
    }

    private static void FromInt64(long[] source, OnnxValue target, int count)
    {
        switch (target.ElementType)
        {
            case OnnxElementType.Float:
            {
                float[] values = target.Floats;
                for (int i = 0; i < count; i++) values[i] = source[i];
                return;
            }

            case OnnxElementType.Int32:
            {
                int[] values = target.Int32s;
                for (int i = 0; i < count; i++) values[i] = unchecked((int)source[i]);
                return;
            }

            case OnnxElementType.Bool:
            {
                bool[] values = target.Booleans;
                for (int i = 0; i < count; i++) values[i] = source[i] != 0L;
                return;
            }

            case OnnxElementType.UInt8:
            {
                byte[] values = target.Bytes;
                for (int i = 0; i < count; i++) values[i] = unchecked((byte)source[i]);
                return;
            }

            case OnnxElementType.Int8:
            {
                sbyte[] values = target.SignedBytes;
                for (int i = 0; i < count; i++) values[i] = unchecked((sbyte)source[i]);
                return;
            }

            default:
                throw Unsupported(OnnxElementType.Int64, target.ElementType);
        }
    }

    private static void FromInt32(int[] source, OnnxValue target, int count)
    {
        switch (target.ElementType)
        {
            case OnnxElementType.Float:
            {
                float[] values = target.Floats;
                for (int i = 0; i < count; i++) values[i] = source[i];
                return;
            }

            case OnnxElementType.Int64:
            {
                long[] values = target.Int64s;
                for (int i = 0; i < count; i++) values[i] = source[i];
                return;
            }

            case OnnxElementType.Bool:
            {
                bool[] values = target.Booleans;
                for (int i = 0; i < count; i++) values[i] = source[i] != 0;
                return;
            }

            case OnnxElementType.UInt8:
            {
                byte[] values = target.Bytes;
                for (int i = 0; i < count; i++) values[i] = unchecked((byte)source[i]);
                return;
            }

            case OnnxElementType.Int8:
            {
                sbyte[] values = target.SignedBytes;
                for (int i = 0; i < count; i++) values[i] = unchecked((sbyte)source[i]);
                return;
            }

            default:
                throw Unsupported(OnnxElementType.Int32, target.ElementType);
        }
    }

    private static void FromBool(bool[] source, OnnxValue target, int count)
    {
        switch (target.ElementType)
        {
            case OnnxElementType.Float:
            {
                float[] values = target.Floats;
                for (int i = 0; i < count; i++) values[i] = source[i] ? 1f : 0f;
                return;
            }

            case OnnxElementType.Int64:
            {
                long[] values = target.Int64s;
                for (int i = 0; i < count; i++) values[i] = source[i] ? 1L : 0L;
                return;
            }

            case OnnxElementType.Int32:
            {
                int[] values = target.Int32s;
                for (int i = 0; i < count; i++) values[i] = source[i] ? 1 : 0;
                return;
            }

            case OnnxElementType.UInt8:
            {
                byte[] values = target.Bytes;
                for (int i = 0; i < count; i++) values[i] = source[i] ? (byte)1 : (byte)0;
                return;
            }

            case OnnxElementType.Int8:
            {
                sbyte[] values = target.SignedBytes;
                for (int i = 0; i < count; i++) values[i] = source[i] ? (sbyte)1 : (sbyte)0;
                return;
            }

            default:
                throw Unsupported(OnnxElementType.Bool, target.ElementType);
        }
    }

    private static void FromByte(byte[] source, OnnxValue target, int count)
    {
        switch (target.ElementType)
        {
            case OnnxElementType.Float:
            {
                float[] values = target.Floats;
                for (int i = 0; i < count; i++) values[i] = source[i];
                return;
            }

            case OnnxElementType.Int64:
            {
                long[] values = target.Int64s;
                for (int i = 0; i < count; i++) values[i] = source[i];
                return;
            }

            case OnnxElementType.Int32:
            {
                int[] values = target.Int32s;
                for (int i = 0; i < count; i++) values[i] = source[i];
                return;
            }

            case OnnxElementType.Int8:
            {
                sbyte[] values = target.SignedBytes;
                for (int i = 0; i < count; i++) values[i] = unchecked((sbyte)source[i]);
                return;
            }

            case OnnxElementType.Bool:
            {
                bool[] values = target.Booleans;
                for (int i = 0; i < count; i++) values[i] = source[i] != 0;
                return;
            }

            default:
                throw Unsupported(OnnxElementType.UInt8, target.ElementType);
        }
    }

    private static void FromSignedByte(sbyte[] source, OnnxValue target, int count)
    {
        switch (target.ElementType)
        {
            case OnnxElementType.Float:
            {
                float[] values = target.Floats;
                for (int i = 0; i < count; i++) values[i] = source[i];
                return;
            }

            case OnnxElementType.Int64:
            {
                long[] values = target.Int64s;
                for (int i = 0; i < count; i++) values[i] = source[i];
                return;
            }

            case OnnxElementType.Int32:
            {
                int[] values = target.Int32s;
                for (int i = 0; i < count; i++) values[i] = source[i];
                return;
            }

            case OnnxElementType.UInt8:
            {
                byte[] values = target.Bytes;
                for (int i = 0; i < count; i++) values[i] = unchecked((byte)source[i]);
                return;
            }

            case OnnxElementType.Bool:
            {
                bool[] values = target.Booleans;
                for (int i = 0; i < count; i++) values[i] = source[i] != 0;
                return;
            }

            default:
                throw Unsupported(OnnxElementType.Int8, target.ElementType);
        }
    }

    private static InferenceException Unsupported(OnnxElementType from, OnnxElementType to) =>
        new InferenceException(
            "This engine does not convert " + OnnxTensor.Name(from) + " to " + OnnxTensor.Name(to) + ".");
}

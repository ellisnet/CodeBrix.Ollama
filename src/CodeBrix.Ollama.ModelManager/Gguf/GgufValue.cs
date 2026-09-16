using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama fs/gguf/keyvalue.go;

/// <summary>
/// One GGUF key-value's value: a scalar, a string, or an array of one element type.
/// </summary>
/// <remarks>
/// The typed accessors convert only within a family, the way Ollama's reader does. A signed integer never
/// reads back through <see cref="AsUInt64"/>, an unsigned integer never through <see cref="AsInt64"/>, and a
/// value of the wrong family reads back as the zero of the requested type rather than throwing.
/// </remarks>
public sealed class GgufValue
{
    private GgufValue(GgufValueType type, GgufValueType arrayElementType, bool isArray, bool isOmittedArray,
        long arrayLength, object rawValue)
    {
        Type = type;
        ArrayElementType = arrayElementType;
        IsArray = isArray;
        IsOmittedArray = isOmittedArray;
        ArrayLength = arrayLength;
        RawValue = rawValue;
    }

    /// <summary>The declared type tag. Arrays carry <see cref="GgufValueType.Array"/>.</summary>
    public GgufValueType Type { get; }

    /// <summary><see langword="true"/> when the value is an array.</summary>
    public bool IsArray { get; }

    /// <summary>The element type of an array. Only meaningful when <see cref="IsArray"/> is <see langword="true"/>.</summary>
    public GgufValueType ArrayElementType { get; }

    /// <summary>
    /// <see langword="true"/> when the value is an array that was skipped because it was longer than
    /// <see cref="GgufReadOptions.MaxArraySize"/>. Its <see cref="RawValue"/> is <see langword="null"/>.
    /// </summary>
    public bool IsOmittedArray { get; }

    /// <summary>The element count of an array, including one that was omitted. Zero for a scalar.</summary>
    public long ArrayLength { get; }

    /// <summary>
    /// The value as stored: a boxed scalar, a <see cref="string"/>, or a typed array such as <c>int[]</c> or
    /// <c>string[]</c>. <see langword="null"/> for an omitted array.
    /// </summary>
    public object RawValue { get; }

    internal static GgufValue CreateScalar(GgufValueType type, object rawValue)
    {
        return new GgufValue(type, type, false, false, 0, rawValue);
    }

    internal static GgufValue CreateArray(GgufValueType elementType, long length, object rawValue)
    {
        return new GgufValue(GgufValueType.Array, elementType, true, false, length, rawValue);
    }

    internal static GgufValue CreateOmittedArray(GgufValueType elementType, long length)
    {
        return new GgufValue(GgufValueType.Array, elementType, true, true, length, null);
    }

    /// <summary>Returns the value as a string, or an empty string when it is not a string.</summary>
    /// <returns>The string value.</returns>
    public string AsString()
    {
        return RawValue as string ?? string.Empty;
    }

    /// <summary>Returns the value as a signed integer, or 0 when it is not a signed integer.</summary>
    /// <returns>The signed value.</returns>
    public long AsInt64()
    {
        return TryGetInt64(out long value) ? value : 0L;
    }

    /// <summary>Returns the value as an unsigned integer, or 0 when it is not an unsigned integer.</summary>
    /// <returns>The unsigned value.</returns>
    public ulong AsUInt64()
    {
        return TryGetUInt64(out ulong value) ? value : 0UL;
    }

    /// <summary>Returns the value as a floating point number, or 0 when it is not a float.</summary>
    /// <returns>The floating point value.</returns>
    public double AsDouble()
    {
        return TryGetDouble(out double value) ? value : 0d;
    }

    /// <summary>Returns the value as a boolean, or <see langword="false"/> when it is not a boolean.</summary>
    /// <returns>The boolean value.</returns>
    public bool AsBoolean()
    {
        return RawValue is bool value && value;
    }

    /// <summary>Gets the value as a signed integer when the stored type is a signed integer.</summary>
    /// <param name="value">Receives the value, or 0 when the stored type is not a signed integer.</param>
    /// <returns><see langword="true"/> when the stored type is a signed integer.</returns>
    public bool TryGetInt64(out long value)
    {
        switch (RawValue)
        {
            case sbyte v: value = v; return true;
            case short v: value = v; return true;
            case int v: value = v; return true;
            case long v: value = v; return true;
            default: value = 0L; return false;
        }
    }

    /// <summary>Gets the value as an unsigned integer when the stored type is an unsigned integer.</summary>
    /// <param name="value">Receives the value, or 0 when the stored type is not an unsigned integer.</param>
    /// <returns><see langword="true"/> when the stored type is an unsigned integer.</returns>
    public bool TryGetUInt64(out ulong value)
    {
        switch (RawValue)
        {
            case byte v: value = v; return true;
            case ushort v: value = v; return true;
            case uint v: value = v; return true;
            case ulong v: value = v; return true;
            default: value = 0UL; return false;
        }
    }

    /// <summary>Gets the value as a floating point number when the stored type is a float.</summary>
    /// <param name="value">Receives the value, or 0 when the stored type is not a float.</param>
    /// <returns><see langword="true"/> when the stored type is a float.</returns>
    public bool TryGetDouble(out double value)
    {
        switch (RawValue)
        {
            case float v: value = v; return true;
            case double v: value = v; return true;
            default: value = 0d; return false;
        }
    }

    /// <summary>Returns the value as a string array, or <see langword="null"/> when it is not one.</summary>
    /// <returns>The strings, or <see langword="null"/>.</returns>
    public string[] AsStringArray()
    {
        return RawValue != null && RawValue.GetType() == typeof(string[]) ? (string[])RawValue : null;
    }

    /// <summary>Returns the value as a boolean array, or <see langword="null"/> when it is not one.</summary>
    /// <returns>The booleans, or <see langword="null"/>.</returns>
    public bool[] AsBooleanArray()
    {
        return RawValue != null && RawValue.GetType() == typeof(bool[]) ? (bool[])RawValue : null;
    }

    /// <summary>Returns the value as a signed integer array, or <see langword="null"/> when it is not one.</summary>
    /// <returns>The signed values widened to 64 bits, or <see langword="null"/>.</returns>
    public long[] AsInt64Array()
    {
        Type type = RawValue?.GetType();
        if (type == typeof(long[]))
        {
            return (long[])RawValue;
        }

        if (type == typeof(sbyte[]))
        {
            var source = (sbyte[])RawValue;
            var result = new long[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        if (type == typeof(short[]))
        {
            var source = (short[])RawValue;
            var result = new long[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        if (type == typeof(int[]))
        {
            var source = (int[])RawValue;
            var result = new long[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        return null;
    }

    /// <summary>Returns the value as an unsigned integer array, or <see langword="null"/> when it is not one.</summary>
    /// <returns>The unsigned values widened to 64 bits, or <see langword="null"/>.</returns>
    public ulong[] AsUInt64Array()
    {
        Type type = RawValue?.GetType();
        if (type == typeof(ulong[]))
        {
            return (ulong[])RawValue;
        }

        if (type == typeof(byte[]))
        {
            var source = (byte[])RawValue;
            var result = new ulong[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        if (type == typeof(ushort[]))
        {
            var source = (ushort[])RawValue;
            var result = new ulong[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        if (type == typeof(uint[]))
        {
            var source = (uint[])RawValue;
            var result = new ulong[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        return null;
    }

    /// <summary>Returns the value as a floating point array, or <see langword="null"/> when it is not one.</summary>
    /// <returns>The values widened to 64 bits, or <see langword="null"/>.</returns>
    public double[] AsDoubleArray()
    {
        Type type = RawValue?.GetType();
        if (type == typeof(double[]))
        {
            return (double[])RawValue;
        }

        if (type == typeof(float[]))
        {
            var source = (float[])RawValue;
            var result = new double[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        return null;
    }

    /// <summary>Returns the scalar formatted with the invariant culture, or <c>"[n items]"</c> for an array.</summary>
    /// <returns>The printable form of the value.</returns>
    public override string ToString()
    {
        if (IsArray)
        {
            return "[" + ArrayLength.ToString(CultureInfo.InvariantCulture) + " items]";
        }

        if (RawValue == null)
        {
            return string.Empty;
        }

        if (RawValue is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return RawValue.ToString();
    }
}

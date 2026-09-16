// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/exec.go and funcs.go (BSD-3-Clause);

/// <summary>
/// Classification and truth rules for the values a rendering template works with, following Go's
/// <c>reflect</c> kinds and its <c>truth</c> function.
/// </summary>
internal static class GoValue
{
    /// <summary>
    /// Classifies a value.
    /// </summary>
    /// <param name="value">The value to classify.</param>
    /// <returns>The value's Go kind.</returns>
    internal static GoKind KindOf(object value)
    {
        switch (value)
        {
            case null:
                return GoKind.Nil;
            case GoUndefined _:
                return GoKind.Invalid;
            case bool _:
                return GoKind.Bool;
            case string _:
                return GoKind.String;
            case int _:
            case long _:
                return GoKind.Int;
            case uint _:
            case ulong _:
                return GoKind.Uint;
            case float _:
            case double _:
                return GoKind.Float;
            case GoSlice _:
                return GoKind.Slice;
            case GoMap _:
                return GoKind.Map;
            case GoStruct _:
                return GoKind.Struct;
            default:
                return GoKind.Invalid;
        }
    }

    /// <summary>
    /// Applies Go's definition of truth: the zero value of a type is false, a non-empty string, slice or
    /// map is true, and a struct is always true.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value counts as true.</returns>
    internal static bool IsTrue(object value)
    {
        switch (KindOf(value))
        {
            case GoKind.Bool:
                return (bool)value;
            case GoKind.Int:
                return ToInt64(value) != 0;
            case GoKind.Uint:
                return ToUInt64(value) != 0;
            case GoKind.Float:
                return ToDouble(value) != 0d;
            case GoKind.String:
                return ((string)value).Length > 0;
            case GoKind.Slice:
                return ((GoSlice)value).Items.Count > 0;
            case GoKind.Map:
                return ((GoMap)value).Entries.Count > 0;
            case GoKind.Struct:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Reports whether a value is nil in the sense Go's comparison helpers use.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is nil or absent.</returns>
    internal static bool IsNil(object value)
    {
        switch (value)
        {
            case null:
            case GoUndefined _:
                return true;
            case GoSlice slice:
                return slice.IsNil;
            case GoMap map:
                return map.IsNil;
            default:
                return false;
        }
    }

    /// <summary>Widens an integer value to <see cref="long"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value as a signed 64-bit integer.</returns>
    internal static long ToInt64(object value) => value is int i ? i : (long)value;

    /// <summary>Widens an unsigned integer value to <see cref="ulong"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value as an unsigned 64-bit integer.</returns>
    internal static ulong ToUInt64(object value) => value is uint u ? u : (ulong)value;

    /// <summary>Widens a floating-point value to <see cref="double"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value as a double.</returns>
    internal static double ToDouble(object value) => value is float f ? f : (double)value;

    /// <summary>
    /// Returns the length Go's <c>len</c> would report: the UTF-8 byte count of a string, otherwise the
    /// element count.
    /// </summary>
    /// <param name="value">The value to measure.</param>
    /// <param name="length">The length, or zero when the value has no length.</param>
    /// <returns><see langword="true"/> when the value has a length.</returns>
    internal static bool TryLength(object value, out int length)
    {
        switch (KindOf(value))
        {
            case GoKind.String:
                length = Encoding.UTF8.GetByteCount((string)value);
                return true;
            case GoKind.Slice:
                length = ((GoSlice)value).Items.Count;
                return true;
            case GoKind.Map:
                length = ((GoMap)value).Entries.Count;
                return true;
            default:
                length = 0;
                return false;
        }
    }

    /// <summary>
    /// Names a value's type the way Go's error messages do.
    /// </summary>
    /// <param name="value">The value to name.</param>
    /// <returns>The type name.</returns>
    internal static string TypeName(object value)
    {
        switch (value)
        {
            case null:
                return "<nil>";
            case GoUndefined _:
                return "invalid";
            case bool _:
                return "bool";
            case string _:
                return "string";
            case int _:
            case long _:
                return "int";
            case uint _:
            case ulong _:
                return "uint";
            case float _:
            case double _:
                return "float64";
            case GoSlice _:
                return "[]interface {}";
            case GoMap _:
                return "map[string]interface {}";
            case GoStruct s:
                return s.TypeName;
            default:
                return value.GetType().Name;
        }
    }
}

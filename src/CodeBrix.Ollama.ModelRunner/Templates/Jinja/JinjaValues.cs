using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The value model the evaluator works on, and the Python semantics that go with it: truthiness,
/// <c>str()</c> and <c>repr()</c> formatting, arithmetic, comparison, membership, attribute and item
/// access, and slicing.
/// </summary>
internal static class JinjaValues
{
    /// <summary>Reports whether a value is the undefined placeholder.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True when the value is undefined.</returns>
    internal static bool IsUndefined(object value) => value is JinjaUndefined;

    /// <summary>
    /// Converts a caller-supplied .NET value into the engine's own model: strings, booleans,
    /// <see cref="long"/>, <see cref="double"/>, null, <see cref="List{T}"/> and an insertion-ordered
    /// dictionary. Any <see cref="IList"/>, <see cref="IDictionary"/>, generic dictionary or
    /// <see cref="JsonElement"/> is accepted and copied.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    internal static object Normalize(object value)
    {
        switch (value)
        {
            case null:
                return null;
            case string _:
            case bool _:
            case long _:
            case double _:
            case JinjaUndefined _:
            case JinjaNamespace _:
            case JinjaMacro _:
            case JinjaFunction _:
            case JinjaBoundMethod _:
                return value;
            case sbyte number:
                return (long)number;
            case byte number:
                return (long)number;
            case short number:
                return (long)number;
            case ushort number:
                return (long)number;
            case int number:
                return (long)number;
            case uint number:
                return (long)number;
            case ulong number:
                return (long)number;
            case float number:
                return (double)number;
            case decimal number:
                return (double)number;
            case JsonElement element:
                return NormalizeJson(element);
        }

        if (TryGetPairs(value, out IEnumerable<KeyValuePair<string, object>> pairs))
        {
            var mapping = NewMapping();
            foreach (KeyValuePair<string, object> pair in pairs)
            {
                mapping[pair.Key] = Normalize(pair.Value);
            }

            return mapping;
        }

        if (value is IEnumerable sequence)
        {
            var list = new List<object>();
            foreach (object item in sequence)
            {
                list.Add(Normalize(item));
            }

            return list;
        }

        return value;
    }

    /// <summary>Creates the insertion-ordered dictionary the engine uses for every mapping.</summary>
    /// <returns>An empty mapping.</returns>
    internal static IDictionary<string, object> NewMapping()
    {
        return new OrderedDictionary<string, object>();
    }

    private static bool TryGetPairs(object value, out IEnumerable<KeyValuePair<string, object>> pairs)
    {
        if (value is IDictionary<string, object> generic)
        {
            pairs = generic;
            return true;
        }

        if (value is IReadOnlyDictionary<string, object> readOnly)
        {
            pairs = readOnly;
            return true;
        }

        if (value is IDictionary weak)
        {
            var list = new List<KeyValuePair<string, object>>();
            foreach (DictionaryEntry entry in weak)
            {
                list.Add(new KeyValuePair<string, object>(
                    Convert.ToString(entry.Key, CultureInfo.InvariantCulture), entry.Value));
            }

            pairs = list;
            return true;
        }

        foreach (Type contract in value.GetType().GetInterfaces())
        {
            if (!contract.IsGenericType)
            {
                continue;
            }

            Type definition = contract.GetGenericTypeDefinition();
            if ((definition != typeof(IDictionary<,>) && definition != typeof(IReadOnlyDictionary<,>))
                || contract.GetGenericArguments()[0] != typeof(string))
            {
                continue;
            }

            var list = new List<KeyValuePair<string, object>>();
            foreach (object entry in (IEnumerable)value)
            {
                Type entryType = entry.GetType();
                object key = entryType.GetProperty("Key").GetValue(entry);
                object item = entryType.GetProperty("Value").GetValue(entry);
                list.Add(new KeyValuePair<string, object>(
                    Convert.ToString(key, CultureInfo.InvariantCulture), item));
            }

            pairs = list;
            return true;
        }

        pairs = null;
        return false;
    }

    private static object NormalizeJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var mapping = NewMapping();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    mapping[property.Name] = NormalizeJson(property.Value);
                }

                return mapping;
            case JsonValueKind.Array:
                var list = new List<object>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    list.Add(NormalizeJson(item));
                }

                return list;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out long integer) ? integer : (object)element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    /// <summary>Applies Python's truthiness rules.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>True when the value is truthy.</returns>
    internal static bool IsTruthy(object value)
    {
        switch (value)
        {
            case null:
            case JinjaUndefined _:
                return false;
            case bool flag:
                return flag;
            case string text:
                return text.Length > 0;
            case long number:
                return number != 0L;
            case double number:
                return number != 0d;
            case IDictionary<string, object> mapping:
                return mapping.Count > 0;
            case IList<object> list:
                return list.Count > 0;
            default:
                return true;
        }
    }

    /// <summary>Formats a value the way Python's <c>str()</c> does.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The text.</returns>
    internal static string ToDisplayString(object value)
    {
        switch (value)
        {
            case null:
                return "None";
            case JinjaUndefined _:
                return string.Empty;
            case string text:
                return text;
            case bool flag:
                return flag ? "True" : "False";
            case long number:
                return number.ToString(CultureInfo.InvariantCulture);
            case double number:
                return FormatDouble(number);
            case IDictionary<string, object> mapping:
                {
                    var builder = new StringBuilder("{");
                    bool first = true;
                    foreach (KeyValuePair<string, object> pair in mapping)
                    {
                        if (!first)
                        {
                            builder.Append(", ");
                        }

                        first = false;
                        builder.Append(ToRepresentation(pair.Key)).Append(": ")
                            .Append(ToRepresentation(pair.Value));
                    }

                    return builder.Append('}').ToString();
                }

            case IList<object> list:
                {
                    var builder = new StringBuilder("[");
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(", ");
                        }

                        builder.Append(ToRepresentation(list[i]));
                    }

                    return builder.Append(']').ToString();
                }

            default:
                return value.ToString();
        }
    }

    /// <summary>Formats a value the way Python's <c>repr()</c> does.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The text.</returns>
    internal static string ToRepresentation(object value)
    {
        if (!(value is string text))
        {
            return ToDisplayString(value);
        }

        char quote = text.IndexOf('\'') >= 0 && text.IndexOf('"') < 0 ? '"' : '\'';
        var builder = new StringBuilder();
        builder.Append(quote);
        foreach (char character in text)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (character == quote)
                    {
                        builder.Append('\\');
                    }

                    builder.Append(character);
                    break;
            }
        }

        return builder.Append(quote).ToString();
    }

    /// <summary>Formats a double the way Python's <c>repr()</c> does.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    internal static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-inf";
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);
        int exponent = text.IndexOfAny(new[] { 'e', 'E' });
        if (exponent < 0)
        {
            return text.IndexOf('.') < 0 ? text + ".0" : text;
        }

        string mantissa = text.Substring(0, exponent);
        string power = text.Substring(exponent + 1);
        char sign = '+';
        if (power[0] == '+' || power[0] == '-')
        {
            sign = power[0];
            power = power.Substring(1);
        }

        power = power.TrimStart('0');
        if (power.Length < 2)
        {
            power = power.PadLeft(2, '0');
        }

        return mantissa + "e" + sign + power;
    }

    /// <summary>Reads a numeric value, treating booleans as 0 and 1 as Python does.</summary>
    /// <param name="value">The value to read.</param>
    /// <param name="number">Receives the value as a double.</param>
    /// <param name="isInteger">Receives true when the value is an integer.</param>
    /// <returns>True when the value is numeric.</returns>
    internal static bool TryGetNumber(object value, out double number, out bool isInteger)
    {
        switch (value)
        {
            case long integer:
                number = integer;
                isInteger = true;
                return true;
            case double real:
                number = real;
                isInteger = false;
                return true;
            case bool flag:
                number = flag ? 1d : 0d;
                isInteger = true;
                return true;
            default:
                number = 0d;
                isInteger = false;
                return false;
        }
    }

    /// <summary>Builds the exception used for every runtime failure.</summary>
    /// <param name="message">What went wrong.</param>
    /// <returns>The exception to throw.</returns>
    internal static ChatTemplateException RuntimeError(string message)
    {
        return new ChatTemplateException("Jinja template error: " + message);
    }

    /// <summary>Applies a binary arithmetic or concatenation operator.</summary>
    /// <param name="operation">The operator.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The result.</returns>
    internal static object Arithmetic(JinjaOperator operation, object left, object right)
    {
        if (operation == JinjaOperator.Concat)
        {
            return ToDisplayString(left) + ToDisplayString(right);
        }

        if (left is JinjaUndefined undefinedLeft)
        {
            throw RuntimeError(undefinedLeft.Describe() + " and cannot be used in arithmetic.");
        }

        if (right is JinjaUndefined undefinedRight)
        {
            throw RuntimeError(undefinedRight.Describe() + " and cannot be used in arithmetic.");
        }

        if (operation == JinjaOperator.Add)
        {
            if (left is string leftText && right is string rightText)
            {
                return leftText + rightText;
            }

            if (left is IList<object> leftList && right is IList<object> rightList)
            {
                var combined = new List<object>(leftList);
                combined.AddRange(rightList);
                return combined;
            }
        }

        if (operation == JinjaOperator.Multiply)
        {
            if (left is string repeated && TryGetNumber(right, out double count, out bool countIsInteger)
                && countIsInteger)
            {
                return count <= 0 ? string.Empty : string.Concat(Enumerable_Repeat(repeated, (int)count));
            }

            if (right is string repeatedRight && TryGetNumber(left, out double leftCount, out bool leftIsInteger)
                && leftIsInteger)
            {
                return leftCount <= 0 ? string.Empty : string.Concat(Enumerable_Repeat(repeatedRight, (int)leftCount));
            }
        }

        if (operation == JinjaOperator.Modulo && left is string format)
        {
            return JinjaFilters.PercentFormat(format, right);
        }

        if (!TryGetNumber(left, out double a, out bool leftInteger)
            || !TryGetNumber(right, out double b, out bool rightInteger))
        {
            throw RuntimeError("unsupported operand types for "
                + Describe(operation) + ": " + DescribeType(left) + " and " + DescribeType(right) + ".");
        }

        bool integer = leftInteger && rightInteger;
        if (integer && TryGetInteger(left, out long x) && TryGetInteger(right, out long y))
        {
            object whole = IntegerArithmetic(operation, x, y);
            if (whole != null)
            {
                return whole;
            }
        }

        switch (operation)
        {
            case JinjaOperator.Add:
                return a + b;
            case JinjaOperator.Subtract:
                return a - b;
            case JinjaOperator.Multiply:
                return a * b;
            case JinjaOperator.Divide:
                if (b == 0d)
                {
                    throw RuntimeError("division by zero.");
                }

                return a / b;
            case JinjaOperator.FloorDivide:
                if (b == 0d)
                {
                    throw RuntimeError("division by zero.");
                }

                return Math.Floor(a / b);
            case JinjaOperator.Modulo:
                if (b == 0d)
                {
                    throw RuntimeError("division by zero.");
                }

                return a - (b * Math.Floor(a / b));
            default:
                return Math.Pow(a, b);
        }
    }

    /// <summary>Reads a value that Python would call an <c>int</c> as a <see cref="long"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="number">Receives the integer.</param>
    /// <returns>True when the value is an integer or a boolean.</returns>
    internal static bool TryGetInteger(object value, out long number)
    {
        switch (value)
        {
            case long integer:
                number = integer;
                return true;
            case bool flag:
                number = flag ? 1L : 0L;
                return true;
            default:
                number = 0L;
                return false;
        }
    }

    private static object IntegerArithmetic(JinjaOperator operation, long left, long right)
    {
        try
        {
            switch (operation)
            {
                case JinjaOperator.Add:
                    return checked(left + right);
                case JinjaOperator.Subtract:
                    return checked(left - right);
                case JinjaOperator.Multiply:
                    return checked(left * right);
                case JinjaOperator.Divide:
                    return null;
                case JinjaOperator.FloorDivide:
                    {
                        if (right == 0L)
                        {
                            throw RuntimeError("division by zero.");
                        }

                        long quotient = checked(left / right);
                        if (left % right != 0L && ((left < 0L) != (right < 0L)))
                        {
                            quotient = checked(quotient - 1L);
                        }

                        return quotient;
                    }

                case JinjaOperator.Modulo:
                    {
                        if (right == 0L)
                        {
                            throw RuntimeError("division by zero.");
                        }

                        long remainder = left % right;
                        if (remainder != 0L && ((remainder < 0L) != (right < 0L)))
                        {
                            remainder = checked(remainder + right);
                        }

                        return remainder;
                    }

                default:
                    return IntegerPower(left, right);
            }
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static object IntegerPower(long value, long exponent)
    {
        if (exponent < 0L)
        {
            return null;
        }

        long result = 1L;
        long factor = value;
        long remaining = exponent;
        while (remaining > 0L)
        {
            if ((remaining & 1L) == 1L)
            {
                result = checked(result * factor);
            }

            remaining >>= 1;
            if (remaining > 0L)
            {
                factor = checked(factor * factor);
            }
        }

        return result;
    }

    private static IEnumerable<string> Enumerable_Repeat(string value, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return value;
        }
    }

    private static string Describe(JinjaOperator operation)
    {
        switch (operation)
        {
            case JinjaOperator.Add:
                return "'+'";
            case JinjaOperator.Subtract:
                return "'-'";
            case JinjaOperator.Multiply:
                return "'*'";
            case JinjaOperator.Divide:
                return "'/'";
            case JinjaOperator.FloorDivide:
                return "'//'";
            case JinjaOperator.Modulo:
                return "'%'";
            default:
                return "'**'";
        }
    }

    /// <summary>Names a value's type the way a Python error message does.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The type name.</returns>
    internal static string DescribeType(object value)
    {
        switch (value)
        {
            case null:
                return "NoneType";
            case JinjaUndefined _:
                return "undefined";
            case string _:
                return "str";
            case bool _:
                return "bool";
            case long _:
                return "int";
            case double _:
                return "float";
            case IDictionary<string, object> _:
                return "dict";
            case IList<object> _:
                return "list";
            default:
                return value.GetType().Name;
        }
    }

    /// <summary>Applies Python's equality rules.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>True when the values are equal.</returns>
    internal static bool AreEqual(object left, object right)
    {
        if (left is JinjaUndefined || right is JinjaUndefined)
        {
            return left is JinjaUndefined && right is JinjaUndefined;
        }

        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        if (left is string leftText)
        {
            return right is string rightText && string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        if (right is string)
        {
            return false;
        }

        if (left is IDictionary<string, object> leftMap)
        {
            if (!(right is IDictionary<string, object> rightMap) || leftMap.Count != rightMap.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, object> pair in leftMap)
            {
                if (!rightMap.TryGetValue(pair.Key, out object other) || !AreEqual(pair.Value, other))
                {
                    return false;
                }
            }

            return true;
        }

        if (left is IList<object> leftList)
        {
            if (!(right is IList<object> rightList) || leftList.Count != rightList.Count)
            {
                return false;
            }

            for (int i = 0; i < leftList.Count; i++)
            {
                if (!AreEqual(leftList[i], rightList[i]))
                {
                    return false;
                }
            }

            return true;
        }

        if (TryGetNumber(left, out double a, out bool _) && TryGetNumber(right, out double b, out bool _))
        {
            return a == b;
        }

        return Equals(left, right);
    }

    /// <summary>Applies Python's ordering rules.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>A negative number, zero or a positive number.</returns>
    internal static int Compare(object left, object right)
    {
        if (left is JinjaUndefined undefined)
        {
            throw RuntimeError(undefined.Describe() + " and cannot be ordered.");
        }

        if (right is JinjaUndefined undefinedRight)
        {
            throw RuntimeError(undefinedRight.Describe() + " and cannot be ordered.");
        }

        if (left is string leftText && right is string rightText)
        {
            return string.CompareOrdinal(leftText, rightText);
        }

        if (TryGetNumber(left, out double a, out bool _) && TryGetNumber(right, out double b, out bool _))
        {
            return a.CompareTo(b);
        }

        if (left is IList<object> leftList && right is IList<object> rightList)
        {
            int shared = Math.Min(leftList.Count, rightList.Count);
            for (int i = 0; i < shared; i++)
            {
                int item = Compare(leftList[i], rightList[i]);
                if (item != 0)
                {
                    return item;
                }
            }

            return leftList.Count.CompareTo(rightList.Count);
        }

        throw RuntimeError("cannot order " + DescribeType(left) + " against " + DescribeType(right) + ".");
    }

    /// <summary>
    /// Applies the <c>in</c> operator. Membership of undefined answers false rather than failing: real
    /// chat templates guard optional variables that way and Jinja's own behaviour there is not worth
    /// a render failure.
    /// </summary>
    /// <param name="container">The container.</param>
    /// <param name="item">The item to look for.</param>
    /// <returns>True when the container holds the item.</returns>
    internal static bool Contains(object container, object item)
    {
        switch (container)
        {
            case JinjaUndefined _:
            case null:
                return false;
            case string text:
                return item is string needle
                    ? text.IndexOf(needle, StringComparison.Ordinal) >= 0
                    : throw RuntimeError("'in' on a string needs a string on the left.");
            case IDictionary<string, object> mapping:
                return item is string key && mapping.ContainsKey(key);
            case IList<object> list:
                foreach (object element in list)
                {
                    if (AreEqual(element, item))
                    {
                        return true;
                    }
                }

                return false;
            default:
                throw RuntimeError("'in' is not supported on " + DescribeType(container) + ".");
        }
    }

    /// <summary>Reads an attribute, following Jinja's "attribute first, then item" order.</summary>
    /// <param name="target">The value to read from.</param>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or undefined.</returns>
    internal static object GetAttribute(object target, string name)
    {
        switch (target)
        {
            case JinjaUndefined _:
            case null:
                return JinjaUndefined.Named(name);
            case JinjaNamespace ns:
                return ns.Get(name);
            case JinjaLoop loop:
                return loop.Get(name);
            case IDictionary<string, object> mapping:
                if (JinjaMethods.IsMappingMethod(name))
                {
                    return new JinjaBoundMethod(mapping, name);
                }

                return mapping.TryGetValue(name, out object value) ? value : JinjaUndefined.Named(name);
            case string _:
                return JinjaMethods.IsStringMethod(name)
                    ? new JinjaBoundMethod(target, name)
                    : (object)JinjaUndefined.Named(name);
            case IList<object> _:
                return JinjaMethods.IsListMethod(name)
                    ? new JinjaBoundMethod(target, name)
                    : (object)JinjaUndefined.Named(name);
            default:
                return JinjaUndefined.Named(name);
        }
    }

    /// <summary>Reads an item, following Jinja's "item first, then attribute" order.</summary>
    /// <param name="target">The value to read from.</param>
    /// <param name="index">The index or key.</param>
    /// <returns>The value, or undefined.</returns>
    internal static object GetItem(object target, object index)
    {
        if (target is JinjaUndefined || target == null)
        {
            return JinjaUndefined.Instance;
        }

        if (target is IDictionary<string, object> mapping && index is string key)
        {
            return mapping.TryGetValue(key, out object value) ? value : GetAttribute(target, key);
        }

        if (TryGetNumber(index, out double position, out bool isInteger) && isInteger && !(index is bool))
        {
            int offset = (int)position;
            if (target is IList<object> list)
            {
                if (offset < 0)
                {
                    offset += list.Count;
                }

                return offset >= 0 && offset < list.Count ? list[offset] : JinjaUndefined.Instance;
            }

            if (target is string text)
            {
                if (offset < 0)
                {
                    offset += text.Length;
                }

                return offset >= 0 && offset < text.Length
                    ? text[offset].ToString()
                    : (object)JinjaUndefined.Instance;
            }
        }

        return index is string name ? GetAttribute(target, name) : JinjaUndefined.Instance;
    }

    /// <summary>Applies a Python slice to a list or a string.</summary>
    /// <param name="target">The value to slice.</param>
    /// <param name="start">The start bound, or null.</param>
    /// <param name="stop">The stop bound, or null.</param>
    /// <param name="step">The step, or null.</param>
    /// <returns>The slice.</returns>
    internal static object Slice(object target, object start, object stop, object step)
    {
        int stepValue = step == null || step is JinjaUndefined ? 1 : ToIndex(step, "slice step");
        if (stepValue == 0)
        {
            throw RuntimeError("a slice step cannot be zero.");
        }

        if (target is string text)
        {
            var builder = new StringBuilder();
            foreach (int index in SliceIndexes(text.Length, start, stop, stepValue))
            {
                builder.Append(text[index]);
            }

            return builder.ToString();
        }

        if (target is IList<object> list)
        {
            var result = new List<object>();
            foreach (int index in SliceIndexes(list.Count, start, stop, stepValue))
            {
                result.Add(list[index]);
            }

            return result;
        }

        if (target is JinjaUndefined undefined)
        {
            throw RuntimeError(undefined.Describe() + " and cannot be sliced.");
        }

        throw RuntimeError(DescribeType(target) + " cannot be sliced.");
    }

    private static IEnumerable<int> SliceIndexes(int length, object start, object stop, int step)
    {
        int begin;
        int end;
        if (step > 0)
        {
            begin = start == null || start is JinjaUndefined ? 0 : Clamp(ToIndex(start, "slice start"), length, false);
            end = stop == null || stop is JinjaUndefined ? length : Clamp(ToIndex(stop, "slice stop"), length, false);
            for (int i = begin; i < end; i += step)
            {
                yield return i;
            }
        }
        else
        {
            begin = start == null || start is JinjaUndefined
                ? length - 1
                : Clamp(ToIndex(start, "slice start"), length, true);
            end = stop == null || stop is JinjaUndefined ? -1 : Clamp(ToIndex(stop, "slice stop"), length, true);
            for (int i = begin; i > end; i += step)
            {
                yield return i;
            }
        }
    }

    private static int Clamp(int value, int length, bool reverse)
    {
        if (value < 0)
        {
            value += length;
        }

        if (reverse)
        {
            if (value < -1)
            {
                value = -1;
            }

            return value > length - 1 ? length - 1 : value;
        }

        if (value < 0)
        {
            value = 0;
        }

        return value > length ? length : value;
    }

    /// <summary>Converts a value to an integer index.</summary>
    /// <param name="value">The value.</param>
    /// <param name="what">What the index is for, used in the error message.</param>
    /// <returns>The index.</returns>
    internal static int ToIndex(object value, string what)
    {
        if (TryGetNumber(value, out double number, out bool isInteger) && isInteger)
        {
            return (int)number;
        }

        throw RuntimeError(what + " must be an integer, not " + DescribeType(value) + ".");
    }

    /// <summary>Materializes a value for iteration, the way a <c>for</c> loop needs it.</summary>
    /// <param name="value">The value to iterate.</param>
    /// <returns>The items.</returns>
    internal static IList<object> Iterate(object value)
    {
        switch (value)
        {
            case JinjaUndefined undefined:
                throw RuntimeError(undefined.Describe() + " and cannot be iterated.");
            case null:
                throw RuntimeError("None cannot be iterated.");
            case string text:
                {
                    var characters = new List<object>(text.Length);
                    foreach (char character in text)
                    {
                        characters.Add(character.ToString());
                    }

                    return characters;
                }

            case IDictionary<string, object> mapping:
                {
                    var keys = new List<object>(mapping.Count);
                    foreach (KeyValuePair<string, object> pair in mapping)
                    {
                        keys.Add(pair.Key);
                    }

                    return keys;
                }

            case IList<object> list:
                return list;
            default:
                throw RuntimeError(DescribeType(value) + " cannot be iterated.");
        }
    }
}

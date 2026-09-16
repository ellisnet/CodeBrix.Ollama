using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/fmt/print.go and format.go (BSD-3-Clause);

/// <summary>
/// The subset of Go's <c>fmt</c> package a chat template can reach: the default <c>%v</c> rendering that
/// printing an action uses, and the <c>print</c>, <c>println</c> and <c>printf</c> builtins.
/// </summary>
internal static class GoFormat
{
    /// <summary>
    /// Renders a value the way Go's <c>%v</c> verb does, honouring a type's own <c>String</c> method.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <returns>The rendered value.</returns>
    internal static string Print(object value) => Print(value, false, false);

    private static string Print(object value, bool plus, bool sharp)
    {
        switch (value)
        {
            case null:
                return "<nil>";
            case GoUndefined _:
                return "<no value>";
            case bool b:
                return b ? "true" : "false";
            case string s:
                return sharp ? GoQuote.Quote(s) : s;
            case int _:
            case long _:
                return GoValue.ToInt64(value).ToString(CultureInfo.InvariantCulture);
            case uint _:
            case ulong _:
                return GoValue.ToUInt64(value).ToString(CultureInfo.InvariantCulture);
            case float _:
            case double _:
                return FormatFloat(GoValue.ToDouble(value), 'g');
            case GoSlice slice:
                return PrintSlice(slice, plus, sharp);
            case GoMap map:
                return PrintMap(map, plus, sharp);
            case GoStruct structure:
                return PrintStruct(structure, plus, sharp);
            default:
                return value.ToString();
        }
    }

    /// <summary>
    /// Joins values the way Go's <c>fmt.Sprint</c> does: a space goes between two operands when neither
    /// of them is a string.
    /// </summary>
    /// <param name="args">The values to join.</param>
    /// <returns>The joined text.</returns>
    internal static string Sprint(IReadOnlyList<object> args)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < args.Count; i++)
        {
            if (i > 0 && !(args[i - 1] is string) && !(args[i] is string))
            {
                builder.Append(' ');
            }

            builder.Append(Print(args[i]));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Joins values the way Go's <c>fmt.Sprintln</c> does: always spaces between operands, and a trailing
    /// newline.
    /// </summary>
    /// <param name="args">The values to join.</param>
    /// <returns>The joined text.</returns>
    internal static string Sprintln(IReadOnlyList<object> args)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < args.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(Print(args[i]));
        }

        builder.Append('\n');
        return builder.ToString();
    }

    /// <summary>
    /// Formats values the way Go's <c>fmt.Sprintf</c> does, for the verbs a chat template uses.
    /// </summary>
    /// <param name="format">The format text.</param>
    /// <param name="args">The values to format.</param>
    /// <returns>The formatted text.</returns>
    internal static string Sprintf(string format, IReadOnlyList<object> args)
    {
        StringBuilder builder = new StringBuilder();
        int argIndex = 0;
        int i = 0;
        string text = format ?? string.Empty;
        while (i < text.Length)
        {
            char c = text[i];
            if (c != '%')
            {
                builder.Append(c);
                i++;
                continue;
            }

            i++;
            if (i >= text.Length)
            {
                builder.Append("%!(NOVERB)");
                break;
            }

            bool minus = false;
            bool plus = false;
            bool zero = false;
            bool space = false;
            bool sharp = false;
            while (i < text.Length)
            {
                char flag = text[i];
                if (flag == '-') { minus = true; i++; continue; }
                if (flag == '+') { plus = true; i++; continue; }
                if (flag == '#') { sharp = true; i++; continue; }
                if (flag == '0') { zero = true; i++; continue; }
                if (flag == ' ') { space = true; i++; continue; }
                break;
            }

            int width = -1;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                width = ((width < 0 ? 0 : width) * 10) + (text[i] - '0');
                i++;
            }

            int precision = -1;
            if (i < text.Length && text[i] == '.')
            {
                i++;
                precision = 0;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9')
                {
                    precision = (precision * 10) + (text[i] - '0');
                    i++;
                }
            }

            if (i >= text.Length)
            {
                builder.Append("%!(NOVERB)");
                break;
            }

            char verb = text[i];
            i++;
            if (verb == '%')
            {
                builder.Append('%');
                continue;
            }

            if (argIndex >= args.Count)
            {
                builder.Append('%');
                builder.Append('!');
                builder.Append(verb);
                builder.Append("(MISSING)");
                continue;
            }

            object arg = args[argIndex++];
            string rendered = FormatVerb(verb, arg, precision, plus, space, sharp);
            int renderedWidth = RuneCount(rendered);
            if (width >= 0 && renderedWidth < width)
            {
                //Go pads to a width in runes, not in UTF-16 code units
                int missing = width - renderedWidth;
                rendered = minus
                    ? rendered + new string(' ', missing)
                    : new string(zero ? '0' : ' ', missing) + rendered;
            }

            builder.Append(rendered);
        }

        if (argIndex < args.Count)
        {
            builder.Append("%!(EXTRA ");
            for (int j = argIndex; j < args.Count; j++)
            {
                if (j > argIndex)
                {
                    builder.Append(", ");
                }

                builder.Append(GoValue.TypeName(args[j]));
                builder.Append('=');
                builder.Append(Print(args[j]));
            }

            builder.Append(')');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats a double the way Go's <c>strconv.FormatFloat</c> does with the shortest representation
    /// that round-trips.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">One of <c>g</c>, <c>e</c> or <c>f</c>.</param>
    /// <returns>The formatted number.</returns>
    internal static string FormatFloat(double value, char format)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "+Inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-Inf";
        }

        bool negative = value < 0 || (value == 0 && double.IsNegative(value));
        ShortestDigits(Math.Abs(value), out string digits, out int decimalPoint);
        string body;
        if (format == 'e' || (format == 'g' && (decimalPoint - 1 < -4 || decimalPoint - 1 >= 6)))
        {
            body = ExponentForm(digits, decimalPoint);
        }
        else
        {
            body = FixedForm(digits, decimalPoint);
        }

        return negative ? "-" + body : body;
    }

    private static string ExponentForm(string digits, int decimalPoint)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append(digits[0]);
        if (digits.Length > 1)
        {
            builder.Append('.');
            builder.Append(digits, 1, digits.Length - 1);
        }

        int exponent = digits == "0" ? 0 : decimalPoint - 1;
        builder.Append('e');
        builder.Append(exponent < 0 ? '-' : '+');
        int magnitude = Math.Abs(exponent);
        builder.Append(magnitude < 10
            ? "0" + magnitude.ToString(CultureInfo.InvariantCulture)
            : magnitude.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static string FixedForm(string digits, int decimalPoint)
    {
        if (digits == "0")
        {
            return "0";
        }

        StringBuilder builder = new StringBuilder();
        if (decimalPoint <= 0)
        {
            builder.Append("0.");
            builder.Append('0', -decimalPoint);
            builder.Append(digits);
            return builder.ToString();
        }

        if (decimalPoint >= digits.Length)
        {
            builder.Append(digits);
            builder.Append('0', decimalPoint - digits.Length);
            return builder.ToString();
        }

        builder.Append(digits, 0, decimalPoint);
        builder.Append('.');
        builder.Append(digits, decimalPoint, digits.Length - decimalPoint);
        return builder.ToString();
    }

    private static void ShortestDigits(double absolute, out string digits, out int decimalPoint)
    {
        if (absolute == 0)
        {
            digits = "0";
            decimalPoint = 1;
            return;
        }

        string round = absolute.ToString("R", CultureInfo.InvariantCulture);
        int exponent = 0;
        int e = round.IndexOfAny(new[] { 'E', 'e' });
        if (e >= 0)
        {
            exponent = int.Parse(round.Substring(e + 1), NumberStyles.Integer, CultureInfo.InvariantCulture);
            round = round.Substring(0, e);
        }

        int point = round.IndexOf('.');
        string mantissa = point < 0 ? round : round.Remove(point, 1);
        int intDigits = point < 0 ? round.Length : point;
        int leading = 0;
        while (leading < mantissa.Length - 1 && mantissa[leading] == '0')
        {
            leading++;
        }

        mantissa = mantissa.Substring(leading);
        intDigits -= leading;
        mantissa = mantissa.TrimEnd('0');
        if (mantissa.Length == 0)
        {
            mantissa = "0";
        }

        digits = mantissa;
        decimalPoint = intDigits + exponent;
    }

    private static string FormatVerb(char verb, object arg, int precision, bool plus, bool space, bool sharp)
    {
        switch (verb)
        {
            case 'v':
                return Print(arg, plus, sharp);
            case 's':
                return Truncate(Print(arg), precision);
            case 'q':
                return FormatQuoted(arg, precision);
            case 't':
                return arg is bool b ? (b ? "true" : "false") : BadVerb(verb, arg);
            case 'd':
                return FormatInteger(verb, arg, 10, false, plus, space);
            case 'b':
                return FormatInteger(verb, arg, 2, false, plus, space);
            case 'o':
                return FormatInteger(verb, arg, 8, false, plus, space);
            case 'x':
                return arg is string hexText
                    ? ToHex(hexText, false)
                    : FormatInteger(verb, arg, 16, false, plus, space);
            case 'X':
                return arg is string hexUpper
                    ? ToHex(hexUpper, true)
                    : FormatInteger(verb, arg, 16, true, plus, space);
            case 'c':
                return FormatRune(arg, false);
            case 'U':
                return FormatRune(arg, true);
            case 'f':
            case 'F':
            case 'e':
            case 'E':
            case 'g':
            case 'G':
                return FormatFloatVerb(verb, arg, precision, plus);
            default:
                return BadVerb(verb, arg);
        }
    }

    private static string BadVerb(char verb, object arg)
        => "%!" + verb + "(" + GoValue.TypeName(arg) + "=" + Print(arg) + ")";

    private static string FormatQuoted(object arg, int precision)
    {
        if (arg is string text)
        {
            return GoQuote.Quote(Truncate(text, precision));
        }

        GoKind kind = GoValue.KindOf(arg);
        if (kind != GoKind.Int && kind != GoKind.Uint)
        {
            return BadVerb('q', arg);
        }

        //Go's %q on an integer is a single-quoted rune, and anything outside the rune range is the
        //replacement character
        ulong value = kind == GoKind.Int ? unchecked((ulong)GoValue.ToInt64(arg)) : GoValue.ToUInt64(arg);
        return GoQuote.QuoteRune(value <= 0x10FFFF ? (int)value : 0xFFFD);
    }

    private static string Truncate(string text, int precision)
    {
        if (precision < 0 || string.IsNullOrEmpty(text))
        {
            return text;
        }

        int index = 0;
        int runes = 0;
        while (index < text.Length && runes < precision)
        {
            index += char.IsHighSurrogate(text[index]) && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
            runes++;
        }

        return index >= text.Length ? text : text.Substring(0, index);
    }

    private static int RuneCount(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }

            count++;
        }

        return count;
    }

    private static string ToHex(string text, bool upper)
    {
        StringBuilder builder = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            builder.Append(b.ToString(upper ? "X2" : "x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string FormatRune(object arg, bool codePointForm)
    {
        GoKind kind = GoValue.KindOf(arg);
        if (kind != GoKind.Int && kind != GoKind.Uint)
        {
            return BadVerb(codePointForm ? 'U' : 'c', arg);
        }

        long codePoint = kind == GoKind.Int ? GoValue.ToInt64(arg) : (long)GoValue.ToUInt64(arg);
        if (codePointForm)
        {
            return "U+" + codePoint.ToString("X4", CultureInfo.InvariantCulture);
        }

        if (codePoint < 0 || codePoint > 0x10FFFF)
        {
            return "�";
        }

        return char.ConvertFromUtf32((int)codePoint);
    }

    private static string FormatInteger(char verb, object arg, int radix, bool upper, bool plus, bool space)
    {
        GoKind kind = GoValue.KindOf(arg);
        if (kind != GoKind.Int && kind != GoKind.Uint)
        {
            return BadVerb(verb, arg);
        }

        bool negative = false;
        ulong magnitude;
        if (kind == GoKind.Int)
        {
            long value = GoValue.ToInt64(arg);
            negative = value < 0;
            magnitude = negative ? (ulong)(-(value + 1)) + 1UL : (ulong)value;
        }
        else
        {
            //an unsigned value above long.MaxValue must keep all 64 bits
            magnitude = GoValue.ToUInt64(arg);
        }

        string digits = radix == 10
            ? magnitude.ToString(CultureInfo.InvariantCulture)
            : ToRadix(magnitude, radix, upper);
        string sign = negative ? "-" : plus ? "+" : space ? " " : string.Empty;
        return sign + digits;
    }

    private static string ToRadix(ulong value, int radix, bool upper)
    {
        if (value == 0)
        {
            return "0";
        }

        const string Lower = "0123456789abcdef";
        const string Upper = "0123456789ABCDEF";
        string alphabet = upper ? Upper : Lower;
        StringBuilder builder = new StringBuilder();
        while (value > 0)
        {
            builder.Insert(0, alphabet[(int)(value % (ulong)radix)]);
            value /= (ulong)radix;
        }

        return builder.ToString();
    }

    private static string FormatFloatVerb(char verb, object arg, int precision, bool plus)
    {
        GoKind kind = GoValue.KindOf(arg);
        double value;
        if (kind == GoKind.Float)
        {
            value = GoValue.ToDouble(arg);
        }
        else if (kind == GoKind.Int)
        {
            value = GoValue.ToInt64(arg);
        }
        else if (kind == GoKind.Uint)
        {
            value = GoValue.ToUInt64(arg);
        }
        else
        {
            return BadVerb(verb, arg);
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            //Go writes NaN, +Inf and -Inf whatever the verb and whatever the precision
            return FormatFloat(value, 'g');
        }

        string body;
        if (precision >= 0)
        {
            switch (char.ToLowerInvariant(verb))
            {
                case 'f':
                    body = value.ToString("F" + precision.ToString(CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture);
                    break;
                case 'e':
                    body = ToGoExponent(value.ToString("E" + precision.ToString(CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture));
                    break;
                default:
                    body = value.ToString("G" + Math.Max(precision, 1).ToString(CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture);
                    body = ToGoExponent(body);
                    break;
            }
        }
        else
        {
            //only %g, %G and %v use the shortest representation that round-trips; %f, %e and %E default
            //to a precision of six, as Go's fmt does
            switch (char.ToLowerInvariant(verb))
            {
                case 'f':
                    body = value.ToString("F6", CultureInfo.InvariantCulture);
                    break;
                case 'e':
                    body = ToGoExponent(value.ToString("E6", CultureInfo.InvariantCulture));
                    break;
                default:
                    body = FormatFloat(value, 'g');
                    break;
            }
        }

        if (verb == 'E' || verb == 'G')
        {
            body = body.Replace("e", "E");
        }

        return plus && value >= 0 ? "+" + body : body;
    }

    private static string ToGoExponent(string text)
    {
        int e = text.IndexOfAny(new[] { 'E', 'e' });
        if (e < 0)
        {
            return text;
        }

        string mantissa = text.Substring(0, e);
        string exponent = text.Substring(e + 1);
        char sign = '+';
        if (exponent.Length > 0 && (exponent[0] == '+' || exponent[0] == '-'))
        {
            sign = exponent[0];
            exponent = exponent.Substring(1);
        }

        exponent = exponent.TrimStart('0');
        if (exponent.Length == 0)
        {
            exponent = "0";
        }

        if (exponent.Length < 2)
        {
            exponent = "0" + exponent;
        }

        return mantissa + "e" + sign + exponent;
    }

    private static string PrintSlice(GoSlice slice, bool plus, bool sharp)
    {
        if (slice.Stringer != null && !sharp)
        {
            return slice.Stringer(slice);
        }

        StringBuilder builder = new StringBuilder();
        builder.Append(sharp ? GoValue.TypeName(slice) + "{" : "[");
        for (int i = 0; i < slice.Items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(sharp ? ", " : " ");
            }

            builder.Append(Print(slice.Items[i], plus, sharp));
        }

        builder.Append(sharp ? '}' : ']');
        return builder.ToString();
    }

    private static string PrintMap(GoMap map, bool plus, bool sharp)
    {
        if (map.Stringer != null && !sharp)
        {
            return map.Stringer(map);
        }

        StringBuilder builder = new StringBuilder();
        builder.Append(sharp ? GoValue.TypeName(map) + "{" : "map[");
        List<string> keys = map.SortedKeys();
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(sharp ? ", " : " ");
            }

            builder.Append(sharp ? GoQuote.Quote(keys[i]) : keys[i]);
            builder.Append(':');
            builder.Append(Print(map.Entries[keys[i]], plus, sharp));
        }

        builder.Append(sharp ? '}' : ']');
        return builder.ToString();
    }

    private static string PrintStruct(GoStruct structure, bool plus, bool sharp)
    {
        //Go's %#v wants the Go-syntax representation, so a String method does not get a say
        if (structure.Stringer != null && !sharp)
        {
            return structure.Stringer(structure);
        }

        StringBuilder builder = new StringBuilder();
        if (sharp)
        {
            builder.Append(structure.TypeName);
        }

        builder.Append('{');
        for (int i = 0; i < structure.Fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(sharp ? ", " : " ");
            }

            if (plus || sharp)
            {
                builder.Append(structure.Fields[i].Name);
                builder.Append(':');
            }

            builder.Append(Print(structure.Fields[i].Value, plus, sharp));
        }

        builder.Append('}');
        return builder.ToString();
    }
}

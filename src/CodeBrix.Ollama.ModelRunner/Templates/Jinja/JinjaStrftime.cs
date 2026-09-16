using System;
using System.Globalization;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The <c>strftime_now</c> implementation. It supports the directives a chat template realistically
/// uses; everything else is copied through unchanged, so an unknown directive never fails a render.
/// </summary>
internal static class JinjaStrftime
{
    /// <summary>Formats a moment with a Python <c>strftime</c> format string.</summary>
    /// <param name="format">The format string.</param>
    /// <param name="moment">The moment to format.</param>
    /// <returns>The formatted text.</returns>
    internal static string Format(string format, DateTime moment)
    {
        if (string.IsNullOrEmpty(format))
        {
            return string.Empty;
        }

        CultureInfo culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length)
            {
                builder.Append(format[i]);
                continue;
            }

            char directive = format[++i];
            switch (directive)
            {
                case 'Y':
                    builder.Append(moment.Year.ToString("D4", culture));
                    break;
                case 'y':
                    builder.Append((moment.Year % 100).ToString("D2", culture));
                    break;
                case 'm':
                    builder.Append(moment.Month.ToString("D2", culture));
                    break;
                case 'd':
                    builder.Append(moment.Day.ToString("D2", culture));
                    break;
                case 'H':
                    builder.Append(moment.Hour.ToString("D2", culture));
                    break;
                case 'I':
                    int hour = moment.Hour % 12;
                    builder.Append((hour == 0 ? 12 : hour).ToString("D2", culture));
                    break;
                case 'M':
                    builder.Append(moment.Minute.ToString("D2", culture));
                    break;
                case 'S':
                    builder.Append(moment.Second.ToString("D2", culture));
                    break;
                case 'f':
                    builder.Append((moment.Millisecond * 1000).ToString("D6", culture));
                    break;
                case 'p':
                    builder.Append(moment.Hour < 12 ? "AM" : "PM");
                    break;
                case 'B':
                    builder.Append(culture.DateTimeFormat.GetMonthName(moment.Month));
                    break;
                case 'b':
                    builder.Append(culture.DateTimeFormat.GetAbbreviatedMonthName(moment.Month));
                    break;
                case 'A':
                    builder.Append(culture.DateTimeFormat.GetDayName(moment.DayOfWeek));
                    break;
                case 'a':
                    builder.Append(culture.DateTimeFormat.GetAbbreviatedDayName(moment.DayOfWeek));
                    break;
                case 'j':
                    builder.Append(moment.DayOfYear.ToString("D3", culture));
                    break;
                case 'Z':
                    builder.Append(TimeZoneInfo.Local.StandardName);
                    break;
                case '%':
                    builder.Append('%');
                    break;
                default:
                    builder.Append('%').Append(directive);
                    break;
            }
        }

        return builder.ToString();
    }
}

using System;
using System.Globalization;

namespace CodeBrix.Ollama.ModelManager.Python.Tests;

/// <summary>
/// What one run of a test-side Python script is worth knowing about afterwards.
/// </summary>
/// <param name="Exited">Whether the script ended within the time it was given.</param>
/// <param name="ExitCode">Its exit code, or -1 when it never ended.</param>
/// <param name="Output">Everything it wrote to standard output.</param>
/// <param name="Errors">Everything it wrote to standard error.</param>
/// <param name="Elapsed">How long it took, in seconds.</param>
public sealed record VenvPythonRun(bool Exited, int ExitCode, string Output, string Errors, double Elapsed)
{
    /// <summary>
    /// Renders the run for an assertion message.
    /// </summary>
    /// <returns>What the script printed, or an empty string when it printed nothing.</returns>
    public string Report()
    {
        string text = (Output + Errors).Trim();
        return text.Length == 0 ? string.Empty : "\nScript output:\n" + text;
    }

    /// <summary>
    /// The value of one <c>key: value</c> line the script printed.
    /// </summary>
    /// <param name="key">The key, without its colon.</param>
    /// <returns>The value, or <see langword="null"/> when the script printed no such line.</returns>
    public string Value(string key)
    {
        string prefix = key + ": ";
        foreach (string line in (Output ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return trimmed.Substring(prefix.Length).Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// The value of one <c>key: value</c> line as a number.
    /// </summary>
    /// <param name="key">The key, without its colon.</param>
    /// <returns>The number, or <see cref="double.NaN"/> when there is no such line or it is not one.</returns>
    public double Number(string key)
    {
        string value = Value(key);
        return value != null
               && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : double.NaN;
    }
}

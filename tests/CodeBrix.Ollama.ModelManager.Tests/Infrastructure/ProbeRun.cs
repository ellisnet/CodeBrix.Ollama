namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// What one run of the probe console application is worth knowing about afterwards.
/// </summary>
/// <param name="Exited">Whether the process ended within the time it was given.</param>
/// <param name="ExitCode">Its exit code, or -1 when it never ended.</param>
/// <param name="Output">Everything it wrote to standard output.</param>
/// <param name="Errors">Everything it wrote to standard error.</param>
/// <param name="Elapsed">How long it took to end, in seconds.</param>
public sealed record ProbeRun(bool Exited, int ExitCode, string Output, string Errors, double Elapsed)
{
    /// <summary>
    /// Renders the run for an assertion message.
    /// </summary>
    /// <returns>The probe's output, or an empty string when it produced none.</returns>
    public string Report()
    {
        string text = (Output + Errors).Trim();
        return text.Length == 0 ? string.Empty : "\nProbe output:\n" + text;
    }

    /// <summary>
    /// Whether standard output carries a line that reads exactly as given.
    /// </summary>
    /// <param name="line">The line to look for, without its ending.</param>
    /// <returns><see langword="true"/> when the probe printed that line.</returns>
    public bool Printed(string line)
    {
        foreach (string written in (Output ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            if (written.Trim() == line)
            {
                return true;
            }
        }
        return false;
    }
}

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// What one run of a child process is worth knowing about afterwards.
/// </summary>
/// <param name="Exited">Whether it ended within the time it was given.</param>
/// <param name="ExitCode">Its exit code, or -1 when it never ended.</param>
/// <param name="Output">Everything it wrote to standard output.</param>
/// <param name="Errors">Everything it wrote to standard error.</param>
/// <param name="Elapsed">How long it took, in seconds.</param>
public sealed record ChildProcessRun(bool Exited, int ExitCode, string Output, string Errors, double Elapsed)
{
    /// <summary>
    /// Renders the run for an assertion message.
    /// </summary>
    /// <returns>What it printed, or an empty string when it printed nothing.</returns>
    public string Report()
    {
        string text = (Output + Errors).Trim();
        return text.Length == 0 ? string.Empty : "\nProcess output:\n" + text;
    }
}

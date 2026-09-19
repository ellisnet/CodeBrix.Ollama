namespace ModelQueryTool.ChatTerminal;

/// <summary>
/// Configuration for a <see cref="ShellSession"/>.
/// </summary>
public sealed class ShellSessionOptions
{
    /// <summary>The root prompt. Default "&gt; ".</summary>
    public string Prompt { get; set; } = "> ";

    /// <summary>
    /// Banner lines written by <see cref="ShellSession.Start"/> before the
    /// first prompt. Null or empty for no banner.
    /// </summary>
    public string[] Banner { get; set; }
}

using System;

namespace ModelQueryTool.ChatTerminal.IO;

/// <summary>
/// An <see cref="IShellIO"/> that forwards everything to a single sink
/// delegate. Used by <see cref="ShellSession"/> to route command output to
/// whatever terminal view is attached.
/// </summary>
public sealed class DelegateShellIO : IShellIO
{
    private readonly Action<string> _sink;

    /// <summary>Creates the IO wrapper around the given sink.</summary>
    public DelegateShellIO(Action<string> sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <inheritdoc/>
    public void Write(string text)
    {
        if (!string.IsNullOrEmpty(text)) { _sink(text); }
    }

    /// <inheritdoc/>
    public void WriteLine(string text = "")
    {
        _sink((text ?? string.Empty) + "\r\n");
    }
}

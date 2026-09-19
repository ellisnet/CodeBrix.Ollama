using System.Threading.Tasks;

namespace ModelQueryTool.ChatTerminal.Commands;

/// <summary>
/// Clears the terminal screen and homes the cursor.
/// </summary>
public sealed class ClearCommand : IShellCommand
{
    /// <inheritdoc/>
    public string Name => "clear";

    /// <inheritdoc/>
    public string Summary => "Clears the terminal screen.";

    /// <inheritdoc/>
    public string Usage => "clear";

    /// <inheritdoc/>
    public Task ExecuteAsync(ShellCommandContext context)
    {
        context.IO.Write("\x1b[2J\x1b[H");
        return Task.CompletedTask;
    }
}

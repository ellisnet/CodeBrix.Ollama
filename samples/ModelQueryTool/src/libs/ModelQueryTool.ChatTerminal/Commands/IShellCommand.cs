using System.Threading.Tasks;

namespace ModelQueryTool.ChatTerminal.Commands;

/// <summary>
/// One command the user can type ("think", "set-system-prompt", "help", …).
/// Implementations are registered with a <see cref="CommandRegistry"/> and
/// dispatched by the session's interpreter — in a chat, by
/// <see cref="ChatLineInterpreter"/> for lines that begin with a slash.
/// </summary>
public interface IShellCommand
{
    /// <summary>
    /// The name the user types, WITHOUT the slash a chat puts in front of it
    /// (lower-case, kebab-case for several words, by convention).
    /// </summary>
    string Name { get; }

    /// <summary>A one-line description shown by the help listing.</summary>
    string Summary { get; }

    /// <summary>
    /// The usage line shown for one command (e.g. "set-system-prompt [-y] "&lt;text&gt;"").
    /// Written without the slash; the help command adds the prefix it is showing.
    /// </summary>
    string Usage { get; }

    /// <summary>Executes the command.</summary>
    Task ExecuteAsync(ShellCommandContext context);
}

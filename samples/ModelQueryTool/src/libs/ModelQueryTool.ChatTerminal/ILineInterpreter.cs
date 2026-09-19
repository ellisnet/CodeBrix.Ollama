using System.Threading;
using System.Threading.Tasks;

namespace ModelQueryTool.ChatTerminal;

/// <summary>
/// Handles submitted lines for a session mode. The root interpreter of a chat
/// is <see cref="ChatLineInterpreter"/>; a command-only session uses
/// <see cref="Commands.CommandInterpreter"/>. A sub-mode pushes its own
/// interpreter with <see cref="ShellSession.PushInterpreter"/> and pops it to
/// leave the mode. Ctrl+D on an empty line pops automatically.
/// </summary>
public interface ILineInterpreter
{
    /// <summary>The prompt shown while this interpreter is active (e.g. "&gt; ").</summary>
    string Prompt { get; }

    /// <summary>
    /// Handles one submitted line. Runs on a worker thread; write results via
    /// session.Output. The token is signalled when the user presses Ctrl+C.
    /// </summary>
    Task HandleLineAsync(ShellSession session, string line, CancellationToken cancellationToken);
}

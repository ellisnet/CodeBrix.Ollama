using ModelQueryTool.ChatTerminal.Commands;
using System;
using System.Threading.Tasks;

namespace ModelQueryTool.Services.Commands;

/// <summary>
/// What every command of this chat has in common: the chat it acts on, and the promise that a
/// failure reaches the user as one red line rather than as a stack trace or a dead session.
/// </summary>
/// <remarks>
/// A cancelled command is not a failure and is let through: the session turns it into the
/// <c>^C</c> the user is expecting.
/// </remarks>
internal abstract class ChatCommandBase : IShellCommand
{
    /// <summary>Creates the command over the chat it acts on.</summary>
    /// <param name="shell">The chat.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> is null.</exception>
    protected ChatCommandBase(ChatShell shell)
    {
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    /// <summary>Gets the chat this command acts on.</summary>
    protected ChatShell Shell { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Summary { get; }

    /// <inheritdoc />
    public abstract string Usage { get; }

    /// <inheritdoc />
    public async Task ExecuteAsync(ShellCommandContext context)
    {
        try
        {
            await RunAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Shell.WriteError(ChatShell.Describe(exception));
        }
    }

    /// <summary>Does what the command does.</summary>
    /// <param name="context">The arguments, the session and the token Ctrl+C signals.</param>
    /// <returns>A task that completes when the command is done.</returns>
    protected abstract Task RunAsync(ShellCommandContext context);
}

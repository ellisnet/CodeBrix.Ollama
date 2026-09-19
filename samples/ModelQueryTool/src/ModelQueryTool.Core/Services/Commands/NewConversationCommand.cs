using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.ChatTerminal.Output;
using System.Threading.Tasks;

namespace ModelQueryTool.Services.Commands;

/// <summary>
/// Starts a new conversation and clears the screen, which is what "clear" means in a chat: the
/// model forgets what has been said, and the screen stops showing it.
/// </summary>
/// <remarks>
/// Ctrl+L still clears the screen alone, so there is a way to tidy the window without making the
/// model forget anything.
/// </remarks>
internal sealed class NewConversationCommand : ChatCommandBase
{
    /// <summary>Creates the command.</summary>
    /// <param name="shell">The chat.</param>
    internal NewConversationCommand(ChatShell shell)
        : base(shell)
    {
    }

    /// <inheritdoc />
    public override string Name => "clear";

    /// <inheritdoc />
    public override string Summary => "Starts a new conversation and clears the screen.";

    /// <inheritdoc />
    public override string Usage => "clear";

    /// <inheritdoc />
    protected override Task RunAsync(ShellCommandContext context)
    {
        if (Shell.RefuseWhenBusy()) { return Task.CompletedTask; }

        Shell.Host.ClearConversation();
        Shell.ForgetConversation();
        Shell.IO.Write(TerminalText.ClearScreen());
        Shell.WriteNotice("New conversation - the model has been told nothing yet.");

        return Task.CompletedTask;
    }
}

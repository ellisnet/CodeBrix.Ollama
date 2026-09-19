using ModelQueryTool.ChatTerminal.Commands;
using System.Threading.Tasks;

namespace ModelQueryTool.Services.Commands;

/// <summary>
/// Sets the instruction every request opens with, or clears it. Without the confirmation it says
/// what setting one would cost and changes nothing at all.
/// </summary>
/// <remarks>
/// The model is NOT reloaded: a system prompt is only the first message of a request, and the
/// weights never see it. What it does cost is the conversation - the answers already given were
/// written under other instructions, and a changed first message leaves the prefix cache nothing
/// to reuse - so a new conversation starts.
/// </remarks>
internal sealed class SetSystemPromptCommand : ChatCommandBase
{
    /// <summary>Creates the command.</summary>
    /// <param name="shell">The chat.</param>
    internal SetSystemPromptCommand(ChatShell shell)
        : base(shell)
    {
    }

    /// <inheritdoc />
    public override string Name => "set-system-prompt";

    /// <inheritdoc />
    public override string Summary => "Sets the instruction every conversation opens with.";

    /// <inheritdoc />
    public override string Usage => "set-system-prompt [-y] \"<text>\"";

    /// <inheritdoc />
    protected override Task RunAsync(ShellCommandContext context)
    {
        //The RAW arguments, never the tokens: the tokenizer drops the quotes inside the text,
        //  which would hand the model something the user did not write.
        var confirmed = CommandArguments.TakeConfirmation(context.RawArguments, out var remainder);
        var text = CommandArguments.StripOuterQuotes(remainder);

        if (!confirmed)
        {
            Shell.WriteNotice(text.Length == 0
                ? "This would clear the system prompt and start a new conversation. The model stays loaded."
                : "This would make the system prompt: " + text);

            if (text.Length > 0)
            {
                Shell.WriteNotice("It starts a new conversation; the model stays loaded and is not reloaded.");
            }

            Shell.WriteNotice("Nothing has changed. Add " + CommandArguments.ConfirmFlag + " to do it: /"
                + Name + " " + CommandArguments.ConfirmFlag + " \"" + text + "\"");

            return Task.CompletedTask;
        }

        if (Shell.RefuseWhenBusy()) { return Task.CompletedTask; }

        Shell.Host.SetSystemPrompt(text);
        Shell.ForgetConversation();
        Shell.WriteNotice(text.Length == 0
            ? "The system prompt is cleared, and a new conversation has started."
            : "The system prompt is set, and a new conversation has started.");

        return Task.CompletedTask;
    }
}

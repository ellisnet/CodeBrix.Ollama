using ModelQueryTool.ChatTerminal.Commands;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace ModelQueryTool.Services.Commands;

/// <summary>
/// Puts the model's own words on the clipboard: the last answer, or the whole conversation.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT EXISTS. Text taken out of the terminal with the mouse carries a line break at every row
/// the word wrapper or the line editor broke, so a copied paragraph arrives in pieces and a copied
/// line of code arrives split. This command copies what the MODEL wrote - the pieces of the turn
/// joined, no wrapping, no reasoning, no escape sequences, the model's own line breaks kept and
/// nothing normalized.
/// </para>
/// <para>
/// It changes nothing, so it needs no confirmation. The clipboard belongs to the head, which the
/// chat reaches through its host; a head with no clipboard says so and nothing is copied.
/// </para>
/// </remarks>
internal sealed class CopyCommand : ChatCommandBase
{
    /// <summary>The one argument this command takes.</summary>
    private const string All = "all";

    /// <summary>Creates the command.</summary>
    /// <param name="shell">The chat.</param>
    internal CopyCommand(ChatShell shell)
        : base(shell)
    {
    }

    /// <inheritdoc />
    public override string Name => "copy";

    /// <inheritdoc />
    public override string Summary => "Copies the model's last answer, or the whole conversation, to the clipboard.";

    /// <inheritdoc />
    public override string Usage => "copy [all]";

    /// <inheritdoc />
    protected override async Task RunAsync(ShellCommandContext context)
    {
        var argument = (context.RawArguments ?? string.Empty).Trim();
        var wholeConversation = string.Equals(argument, All, StringComparison.OrdinalIgnoreCase);

        if (argument.Length > 0 && !wholeConversation)
        {
            Shell.WriteError("'" + argument + "' is not something to copy. Usage: /" + Usage);

            return;
        }

        var copy = wholeConversation ? Shell.WholeConversation() : Shell.LastAnswer();

        if (copy.IsEmpty)
        {
            Shell.WriteNotice(wholeConversation
                ? "There is nothing to copy yet - this conversation has no answers in it."
                : "There is nothing to copy yet - the model has not answered anything.");

            return;
        }

        if (!await Shell.CopyToClipboardAsync(copy.Text).ConfigureAwait(false))
        {
            Shell.WriteError("The clipboard could not be reached, so nothing was copied.");

            return;
        }

        Shell.WriteNotice(Describe(copy, wholeConversation));
    }

    /// <summary>
    /// The one line that says what was copied and how much of it. It never quotes the text
    /// itself: the point of the command is that the text is on the clipboard, not on the screen.
    /// </summary>
    /// <param name="copy">What was copied.</param>
    /// <param name="wholeConversation">Whether it was the conversation rather than one answer.</param>
    /// <returns>The line to write.</returns>
    private static string Describe(ChatCopyText copy, bool wholeConversation)
    {
        var characters = copy.Text.Length.ToString("N0", CultureInfo.InvariantCulture);

        if (!wholeConversation)
        {
            return copy.IsComplete
                ? "The last answer is on the clipboard - " + characters + " characters."
                : "The last answer is on the clipboard - " + characters
                    + " characters, and it was incomplete.";
        }

        var turns = copy.TurnCount.ToString(CultureInfo.InvariantCulture)
            + (copy.TurnCount == 1 ? " turn, " : " turns, ");

        return copy.IsComplete
            ? "The conversation is on the clipboard - " + turns + characters + " characters."
            : "The conversation is on the clipboard - " + turns + characters
                + " characters, and an answer in it was incomplete.";
    }
}

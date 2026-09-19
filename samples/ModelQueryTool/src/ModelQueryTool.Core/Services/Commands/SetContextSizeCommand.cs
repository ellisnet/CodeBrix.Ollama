using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.ModelRunning;
using System.Globalization;
using System.Threading.Tasks;

namespace ModelQueryTool.Services.Commands;

/// <summary>
/// Loads the model again at another context size. Without the confirmation it says what that
/// would cost and changes nothing at all.
/// </summary>
/// <remarks>
/// The context size is fixed while a model is loaded - the key/value cache is built to it - so
/// changing it means unloading and loading again, which also ends the conversation. When the new
/// size will not load, the model host puts the previous size back and says so, so a number that
/// is too large never leaves the user with nothing.
/// </remarks>
internal sealed class SetContextSizeCommand : ChatCommandBase
{
    /// <summary>Creates the command.</summary>
    /// <param name="shell">The chat.</param>
    internal SetContextSizeCommand(ChatShell shell)
        : base(shell)
    {
    }

    /// <inheritdoc />
    public override string Name => "set-context-size";

    /// <inheritdoc />
    public override string Summary => "Reloads the model with a different context size.";

    /// <inheritdoc />
    public override string Usage => "set-context-size [-y] <tokens>";

    /// <inheritdoc />
    protected override async Task RunAsync(ShellCommandContext context)
    {
        var confirmed = CommandArguments.TakeConfirmation(context.RawArguments, out var remainder);
        var argument = remainder.Trim();

        if (argument.Length == 0)
        {
            Shell.WriteNotice("The context size is "
                + Shell.Host.ContextSize.ToString(CultureInfo.InvariantCulture) + " tokens. Usage: /" + Usage);

            return;
        }

        if (!uint.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var contextSize))
        {
            Shell.WriteError("'" + argument + "' is not a whole number of tokens. Usage: /" + Usage);

            return;
        }

        if (Shell.Host.State != ModelHostState.Ready)
        {
            Shell.WriteNotice("The context size can only be changed while a model is loaded.");

            return;
        }

        var trained = Shell.Host.TrainedContextSize;
        if (contextSize < ModelHostOptions.MinimumContextSize
            || (trained > 0 && contextSize > (uint)trained))
        {
            Shell.WriteError("A context size is between "
                + ModelHostOptions.MinimumContextSize.ToString(CultureInfo.InvariantCulture) + " and "
                + trained.ToString(CultureInfo.InvariantCulture) + " tokens, which is what this model was trained for.");

            return;
        }

        if (!confirmed)
        {
            Shell.WriteNotice("This would unload the model and load it again with a context of "
                + contextSize.ToString(CultureInfo.InvariantCulture)
                + " tokens, which starts a new conversation.");
            Shell.WriteNotice("Nothing has changed. Add " + CommandArguments.ConfirmFlag + " to do it: /"
                + Name + " " + CommandArguments.ConfirmFlag + " "
                + contextSize.ToString(CultureInfo.InvariantCulture));

            return;
        }

        if (Shell.RefuseWhenBusy()) { return; }

        await Shell.ReloadModelAsync(contextSize, context.CancellationToken).ConfigureAwait(false);
        Shell.WriteNotice("The model is loaded again with a context of "
            + contextSize.ToString(CultureInfo.InvariantCulture) + " tokens, and a new conversation.");
    }
}

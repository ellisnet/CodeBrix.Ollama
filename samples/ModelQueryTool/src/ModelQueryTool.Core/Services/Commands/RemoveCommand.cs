using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.Helpers;
using ModelQueryTool.ModelAccess;
using ModelQueryTool.ModelAccess.Models;
using ModelQueryTool.ModelRunning;
using System;
using System.Threading.Tasks;

namespace ModelQueryTool.Services.Commands;

/// <summary>
/// Takes the model off the disk again, folders and all. Without the confirmation it LOOKS FIRST
/// and then says exactly what would go - or that there is nothing there to go - and deletes
/// nothing.
/// </summary>
/// <remarks>
/// <para>
/// The unconfirmed form calls <see cref="IModelStager.CheckAsync"/>, which is read-only by
/// contract. Nothing that CHANGES anything is called without the confirmation: neither the
/// removal itself nor the stop that would precede it.
/// </para>
/// <para>
/// A loaded model is stopped first: the files a running engine has open cannot be deleted
/// cleanly, and what is on disk is the only copy the application knows about.
/// </para>
/// </remarks>
internal sealed class RemoveCommand : ChatCommandBase
{
    /// <summary>Creates the command.</summary>
    /// <param name="shell">The chat.</param>
    internal RemoveCommand(ChatShell shell)
        : base(shell)
    {
    }

    /// <inheritdoc />
    public override string Name => "remove";

    /// <inheritdoc />
    public override string Summary => "Deletes the model and every folder made for it.";

    /// <inheritdoc />
    public override string Usage => "remove [-y]";

    /// <inheritdoc />
    protected override async Task RunAsync(ShellCommandContext context)
    {
        var confirmed = CommandArguments.TakeConfirmation(context.RawArguments, out var remainder);

        if (remainder.Trim().Length > 0)
        {
            Shell.WriteError("This command takes no argument. Usage: /" + Usage);

            return;
        }

        if (!confirmed)
        {
            await DescribeAsync(context).ConfigureAwait(false);

            return;
        }

        if (Shell.RefuseWhenBusy()) { return; }

        if (Shell.Host.State != ModelHostState.Stopped)
        {
            Shell.WriteNotice("Stopping the model first...");
            await Shell.Host.StopAsync().ConfigureAwait(false);
        }

        var status = await Shell.Stager.RemoveAsync(context.CancellationToken).ConfigureAwait(false);

        Shell.WriteNotice(status.State == ModelStagingState.NotPresent && status.BytesOnDisk == 0L
            ? status.Model.DisplayName + " is gone, and so is every folder made for it."
            : "What is left: " + status.State + " - " + status.Detail + " ("
                + ByteSize.Describe(status.BytesOnDisk) + " in " + status.StoreDirectory + ").");
    }

    /// <summary>
    /// Says what a removal would take away, having looked at the store to find out - including the
    /// case where there is nothing there and the honest answer is "there is nothing to remove".
    /// </summary>
    /// <param name="context">The command's context.</param>
    /// <returns>A task that completes when the answer has been written.</returns>
    private async Task DescribeAsync(ShellCommandContext context)
    {
        ModelStagingStatus status = null;
        Exception failure = null;

        try
        {
            status = await Shell.Stager.CheckAsync(context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (status != null && status.State == ModelStagingState.NotPresent && status.BytesOnDisk == 0L)
        {
            Shell.WriteNotice("There is nothing to remove - no part of " + status.Model.DisplayName
                + " is on disk.");
            Shell.WriteNotice("The folder it would have been in: " + status.StoreDirectory);

            return;
        }

        Shell.WriteNotice("This would delete " + Shell.Stager.Model.DisplayName + " and every folder made "
            + "for it, down to " + Shell.Stager.RootDirectory + " itself, leaving nothing of it on the machine.");

        if (status != null)
        {
            Shell.WriteNotice(ByteSize.Describe(status.BytesOnDisk) + " of it is on disk now.");
        }

        Shell.WriteNotice("A loaded model is stopped first, and getting it back means downloading it again.");
        Shell.WriteNotice("Nothing has been deleted. Add " + CommandArguments.ConfirmFlag + " to do it: /"
            + Name + " " + CommandArguments.ConfirmFlag);

        if (failure != null)
        {
            Shell.WriteNotice("The model folder could not be read: " + ChatShell.Describe(failure));
        }
    }
}

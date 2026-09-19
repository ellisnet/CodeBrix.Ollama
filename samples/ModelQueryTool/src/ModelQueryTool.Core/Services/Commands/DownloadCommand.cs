using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.Helpers;
using ModelQueryTool.ModelAccess;
using ModelQueryTool.ModelAccess.Models;
using ModelQueryTool.ModelRunning;
using System;
using System.Threading.Tasks;

namespace ModelQueryTool.Services.Commands;

/// <summary>
/// Obtains the model. Without the confirmation it LOOKS FIRST and then says what a download would
/// actually do - fetch the whole thing, carry an interrupted one on, repair a damaged one, or
/// nothing at all because the model is already there - and changes nothing.
/// </summary>
/// <remarks>
/// <para>
/// The unconfirmed form calls <see cref="IModelStager.CheckAsync"/>, which is read-only by
/// contract: it opens nothing it does not close and creates neither a file nor a folder. Nothing
/// that CHANGES anything is called without the confirmation - not the download, not the load, not
/// the removal - which is the whole promise of the <c>-y</c> rule.
/// </para>
/// <para>
/// The confirmed form is the only thing in the application that ever reaches the network, and it
/// only does so because the user asked for it in so many words.
/// </para>
/// </remarks>
internal sealed class DownloadCommand : ChatCommandBase
{
    /// <summary>Creates the command.</summary>
    /// <param name="shell">The chat.</param>
    internal DownloadCommand(ChatShell shell)
        : base(shell)
    {
    }

    /// <inheritdoc />
    public override string Name => "download";

    /// <inheritdoc />
    public override string Summary => "Downloads the model, or says what downloading it would involve.";

    /// <inheritdoc />
    public override string Usage => "download [-y]";

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

        var status = await Shell.Stager.CheckAsync(context.CancellationToken).ConfigureAwait(false);

        if (status.State == ModelStagingState.Ready)
        {
            Shell.WriteNotice(status.Model.DisplayName + " is already downloaded ("
                + ByteSize.Describe(status.BytesOnDisk) + " in " + status.StoreDirectory + ").");

            if (Shell.Host.State != ModelHostState.Stopped && Shell.Host.State != ModelHostState.Faulted)
            {
                return;
            }

            await Shell.LoadModelAsync(status.ModelPath, context.CancellationToken).ConfigureAwait(false);
            Shell.WriteNotice("The model is loaded and ready.");

            return;
        }

        await Shell.DownloadAndLoadAsync(context.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Says what a download would do, having looked at the store to find out. It is worth looking:
    /// telling somebody that twenty-one gibibytes would be fetched when the model is already on
    /// their disk is simply wrong.
    /// </summary>
    /// <param name="context">The command's context.</param>
    /// <returns>A task that completes when the answer has been written.</returns>
    private async Task DescribeAsync(ShellCommandContext context)
    {
        ModelStagingStatus status;

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
            DescribeAFreshDownload();
            Shell.WriteNotice("The model folder could not be read: " + ChatShell.Describe(exception));

            return;
        }

        switch (status.State)
        {
            case ModelStagingState.Ready when IsLoaded():
                Shell.WriteNotice(status.Model.DisplayName
                    + " is already downloaded and loaded - there is nothing to do.");
                Shell.WriteNotice(ByteSize.Describe(status.BytesOnDisk) + " is in "
                    + status.StoreDirectory + ".");
                break;

            case ModelStagingState.Ready:
                Shell.WriteNotice(status.Model.DisplayName
                    + " is already downloaded; nothing would be fetched.");
                Shell.WriteNotice(ByteSize.Describe(status.BytesOnDisk) + " is in "
                    + status.StoreDirectory + ".");
                Shell.WriteNotice("/download -y only loads it.");
                break;

            case ModelStagingState.Incomplete:
                Shell.WriteNotice("A download of " + status.Model.DisplayName + " was interrupted: "
                    + status.Detail);
                Shell.WriteNotice(ByteSize.DescribeInUnitOf(status.BytesOnDisk, status.ExpectedTotalBytes)
                    + " of " + ByteSize.Describe(status.ExpectedTotalBytes) + " is already in "
                    + status.StoreDirectory + ".");
                Shell.WriteNotice("Nothing has been fetched. /download -y carries on from there.");
                break;

            case ModelStagingState.Damaged:
                Shell.WriteNotice(status.Model.DisplayName + " is on disk but is not usable: "
                    + status.Detail);
                Shell.WriteNotice("Nothing has been fetched. /download -y repairs it.");
                Shell.WriteNotice("If that does not help, /remove -y and then /download -y.");
                break;

            default:
                DescribeAFreshDownload();
                break;
        }
    }

    /// <summary>What a download of a model that is not there at all would involve.</summary>
    private void DescribeAFreshDownload()
    {
        var model = Shell.Stager.Model;

        Shell.WriteNotice("This would fetch " + model.DisplayName + " - about "
            + ByteSize.Describe(model.ExpectedTotalBytes)
            + ", weights and vision projector together - from its publisher into "
            + Shell.Stager.StoreDirectory + ".");
        Shell.WriteNotice("A download that has already begun carries on from where it stopped, and one that is "
            + "already complete costs nothing. The model is loaded afterwards, which is when it can answer.");
        Shell.WriteNotice("Nothing has been fetched. Add " + CommandArguments.ConfirmFlag + " to start it: /"
            + Name + " " + CommandArguments.ConfirmFlag);
    }

    /// <summary>
    /// Whether a model is loaded right now. Every member of the host throws once it has been put
    /// away, property getters included, so the chat is asked first.
    /// </summary>
    private bool IsLoaded() => !Shell.IsShutDown && Shell.Host.State == ModelHostState.Ready;
}

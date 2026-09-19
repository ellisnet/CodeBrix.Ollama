using ModelQueryTool.ChatTerminal.Commands;
using System.Threading.Tasks;

namespace ModelQueryTool.Services.Commands;

/// <summary>Stops the model and closes the application.</summary>
/// <remarks>
/// The model is unloaded before the window goes, rather than left to the operating system: it
/// holds many gibibytes of native memory, and the same unload runs when the window is closed
/// instead, so whichever way the user leaves, the model is put away the same way.
/// </remarks>
internal sealed class ExitCommand : ChatCommandBase
{
    /// <summary>Creates the command.</summary>
    /// <param name="shell">The chat.</param>
    internal ExitCommand(ChatShell shell)
        : base(shell)
    {
    }

    /// <inheritdoc />
    public override string Name => "exit";

    /// <inheritdoc />
    public override string Summary => "Stops the model and closes the application.";

    /// <inheritdoc />
    public override string Usage => "exit";

    /// <inheritdoc />
    protected override async Task RunAsync(ShellCommandContext context)
    {
        Shell.WriteNotice("Stopping the model and closing...");
        await Shell.ShutdownAsync().ConfigureAwait(false);
        Shell.RequestExit();
    }
}

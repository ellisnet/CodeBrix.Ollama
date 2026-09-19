using ModelQueryTool.ChatTerminal.Commands;
using System;
using System.Threading.Tasks;

namespace ModelQueryTool.Services.Commands;

/// <summary>
/// Turns the model's reasoning on or off, and says which it is when asked without an argument.
/// </summary>
/// <remarks>
/// Reasoning costs nothing on disk and nothing in the conversation - it is a flag on the next
/// request - so this command needs no confirmation and takes effect from the next turn.
/// </remarks>
internal sealed class ThinkCommand : ChatCommandBase
{
    private const string On = "on";
    private const string Off = "off";

    /// <summary>Creates the command.</summary>
    /// <param name="shell">The chat.</param>
    internal ThinkCommand(ChatShell shell)
        : base(shell)
    {
    }

    /// <inheritdoc />
    public override string Name => "think";

    /// <inheritdoc />
    public override string Summary => "Turns the model's reasoning on or off.";

    /// <inheritdoc />
    public override string Usage => "think [on|off]";

    /// <inheritdoc />
    protected override Task RunAsync(ShellCommandContext context)
    {
        var argument = (context.RawArguments ?? string.Empty).Trim();

        if (argument.Length == 0)
        {
            Shell.WriteNotice("Thinking is " + (Shell.Host.Think ? On : Off) + ".");

            return Task.CompletedTask;
        }

        if (string.Equals(argument, On, StringComparison.OrdinalIgnoreCase))
        {
            Shell.Host.Think = true;
            Shell.WriteNotice("Thinking is on; the model reasons before it answers.");

            return Task.CompletedTask;
        }

        if (string.Equals(argument, Off, StringComparison.OrdinalIgnoreCase))
        {
            Shell.Host.Think = false;
            Shell.WriteNotice("Thinking is off; the model answers at once.");

            return Task.CompletedTask;
        }

        Shell.WriteError("'" + argument + "' is neither on nor off. Usage: /" + Usage);

        return Task.CompletedTask;
    }
}

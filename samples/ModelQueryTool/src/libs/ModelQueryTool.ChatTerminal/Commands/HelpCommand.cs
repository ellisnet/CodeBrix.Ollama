using System;
using System.Linq;
using System.Threading.Tasks;
using ModelQueryTool.ChatTerminal.Output;

namespace ModelQueryTool.ChatTerminal.Commands;

/// <summary>
/// Lists the registered commands, or shows one command's usage when invoked
/// as 'help &lt;command&gt;'.
/// </summary>
/// <remarks>
/// <para>
/// Commands are registered under a bare name, but a chat is typed with a slash
/// in front of every one of them. The listing therefore shows the names the way
/// the user has to type them: pass "/" as the prefix and every line reads
/// <c>/think</c>, <c>/set-system-prompt</c> and so on.
/// </para>
/// <para>
/// Given a <see cref="CommandHighlights"/> as well, every name in the listing is
/// shown in the highlight colour. The names are padded to their column BEFORE
/// they are highlighted, so the summaries still line up: an escape sequence is
/// no cells wide but is many characters long.
/// </para>
/// </remarks>
public sealed class HelpCommand : IShellCommand
{
    private readonly CommandRegistry _registry;
    private readonly string _namePrefix;
    private readonly CommandHighlights _highlights;

    /// <summary>Creates the command over the registry it describes.</summary>
    /// <param name="registry">The commands to list.</param>
    /// <param name="namePrefix">
    /// What the user types in front of a command name - "/" in a chat, nothing
    /// in a command-only session.
    /// </param>
    /// <param name="highlights">
    /// Which commands to show in the highlight colour; null writes the listing
    /// in no colour at all.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null.</exception>
    public HelpCommand(CommandRegistry registry, string namePrefix = "", CommandHighlights highlights = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _namePrefix = namePrefix ?? string.Empty;
        _highlights = highlights;
    }

    /// <inheritdoc/>
    public string Name => "help";

    /// <inheritdoc/>
    public string Summary => "Lists commands, or shows usage for one command.";

    /// <inheritdoc/>
    public string Usage => "help [<command>]";

    /// <inheritdoc/>
    public Task ExecuteAsync(ShellCommandContext context)
    {
        if (context.Arguments.Count > 0)
        {
            var name = context.Arguments[0].TrimStart('/');
            if (_registry.TryGet(name, out var command))
            {
                WriteLine(context, _namePrefix + command.Name + " - " + command.Summary);
                WriteLine(context, "usage: " + _namePrefix + command.Usage);
            }
            else
            {
                WriteLine(context, $"Unknown command: {_namePrefix}{name}");
            }

            return Task.CompletedTask;
        }

        var all = _registry.All;
        if (all.Count == 0)
        {
            WriteLine(context, "No commands are registered.");
            return Task.CompletedTask;
        }

        var width = all.Max(c => c.Name.Length) + _namePrefix.Length + 2;

        WriteLine(context, "Available commands:");
        context.IO.WriteLine();
        foreach (var command in all)
        {
            WriteLine(context, "  " + (_namePrefix + command.Name).PadRight(width) + command.Summary);
        }

        context.IO.WriteLine();
        WriteLine(context, $"Type '{_namePrefix}{Name} <command>' for usage.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes one line of the help, with the commands in it highlighted when the
    /// caller asked for that. With no highlights the line is written exactly as
    /// it was built.
    /// </summary>
    private void WriteLine(ShellCommandContext context, string line) =>
        context.IO.WriteLine(TerminalText.Plain(line, _highlights));
}

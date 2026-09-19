using System.Collections.Generic;
using System.Threading;
using ModelQueryTool.ChatTerminal.IO;

namespace ModelQueryTool.ChatTerminal.Commands;

/// <summary>
/// Everything a command execution gets to work with: its arguments, the
/// session, the output surface, and a cancellation token that is signalled
/// when the user presses Ctrl+C while the command runs.
/// </summary>
public sealed class ShellCommandContext
{
    internal ShellCommandContext(ShellSession session, IReadOnlyList<string> arguments,
        string rawArguments, CancellationToken cancellationToken)
    {
        Session = session;
        Arguments = arguments;
        RawArguments = rawArguments ?? string.Empty;
        CancellationToken = cancellationToken;
    }

    /// <summary>The session the command runs in.</summary>
    public ShellSession Session { get; }

    /// <summary>The arguments after the command name, tokenized.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// The text after the command name, exactly as it was typed — quotes, backslashes
    /// and runs of whitespace all still in it.
    /// </summary>
    /// <remarks>
    /// TOKENIZING LOSES CHARACTERS THAT MATTER TO THE TEXT A CHAT SENDS ON.
    /// <see cref="CommandLineTokenizer"/> treats a double quote as grouping and drops
    /// it, which is right for a file name and wrong for a prompt the user is writing:
    /// <c>set-system-prompt -y "answer in the style of a "friendly" tutor"</c> comes
    /// back through <see cref="Arguments"/> with the inner quotes gone, so the model
    /// is given text the user did not write rather than the command failing outright.
    /// A command whose argument is text destined for the model reads it from here and
    /// never from the tokens.
    /// </remarks>
    public string RawArguments { get; }

    /// <summary>Signalled when the user cancels the running command with Ctrl+C.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>The output surface to write results to.</summary>
    public IShellIO IO => Session.Output;
}

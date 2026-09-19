using CodeBrix.Ollama.ModelRunner;
using ModelQueryTool.ChatTerminal.Commands;
using ModelQueryTool.Helpers;
using System.Globalization;
using System.Threading.Tasks;

namespace ModelQueryTool.Services.Commands;

/// <summary>
/// Reports the three things a person wants to know: what is on disk, what is in memory, and what
/// the last turn cost.
/// </summary>
/// <remarks>
/// It reads and changes nothing, so it answers while a download or a load is running - which is
/// exactly when it is asked most.
/// </remarks>
internal sealed class StatusCommand : ChatCommandBase
{
    private const string None = "(none)";
    private const int LabelWidth = 12;

    /// <summary>Creates the command.</summary>
    /// <param name="shell">The chat.</param>
    internal StatusCommand(ChatShell shell)
        : base(shell)
    {
    }

    /// <inheritdoc />
    public override string Name => "status";

    /// <inheritdoc />
    public override string Summary => "Reports the model on disk, the model in memory and the last turn.";

    /// <inheritdoc />
    public override string Usage => "status";

    /// <inheritdoc />
    protected override async Task RunAsync(ShellCommandContext context)
    {
        if (Shell.IsShutDown)
        {
            Shell.WriteNotice("The model has been unloaded and the application is closing.");

            return;
        }

        await ReportModelOnDiskAsync(context).ConfigureAwait(false);
        ReportModelInMemory();
        ReportLastTurn();
    }

    private async Task ReportModelOnDiskAsync(ShellCommandContext context)
    {
        var status = await Shell.Stager.CheckAsync(context.CancellationToken).ConfigureAwait(false);

        Shell.WriteLine("Model on disk");
        Row("model", status.Model.DisplayName);
        Row("state", status.State.ToString());
        Row("folder", status.StoreDirectory);
        Row("on disk", ByteSize.Describe(status.BytesOnDisk));

        if (status.Detail.Length > 0)
        {
            Row("detail", status.Detail);
        }

        Shell.WriteLine(string.Empty);
    }

    private void ReportModelInMemory()
    {
        var host = Shell.Host;
        var trained = host.TrainedContextSize;

        Shell.WriteLine("Model in memory");
        Row("state", host.State.ToString());
        Row("model", host.Details == null ? None : Describe(host.Details));
        Row("context", host.ContextSize.ToString("N0", CultureInfo.InvariantCulture) + " tokens ("
            + host.ConversationTokens.ToString("N0", CultureInfo.InvariantCulture) + " used by the conversation)");
        Row("trained for", trained > 0
            ? trained.ToString("N0", CultureInfo.InvariantCulture) + " tokens"
            : "(not known until a model is loaded)");
        Row("turns", host.TurnCount.ToString(CultureInfo.InvariantCulture));
        Row("thinking", host.Think ? "on" : "off");
        Row("system", string.IsNullOrEmpty(host.SystemPrompt) ? None : host.SystemPrompt);
        Shell.WriteLine(string.Empty);
    }

    private void ReportLastTurn()
    {
        var statistics = Shell.LastStatistics;

        Shell.WriteLine("Last turn");

        if (statistics == null)
        {
            Row("turn", "(none yet)");

            return;
        }

        Row("prompt", statistics.PromptTokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens ("
            + statistics.CachedPromptTokens.ToString("N0", CultureInfo.InvariantCulture) + " of them cached) read in "
            + Seconds(statistics.PromptDuration.TotalSeconds));
        Row("answer", statistics.GeneratedTokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens at "
            + statistics.TokensPerSecond.ToString("0.0", CultureInfo.InvariantCulture) + " a second");
        Row("altogether", Seconds(statistics.TotalDuration.TotalSeconds));
        Row("ended", Shell.LastFinishReason.ToString());
    }

    /// <summary>The engine's own one-line description of the loaded file, falling back to its name and path.</summary>
    private static string Describe(ModelDetails details)
    {
        if (!string.IsNullOrWhiteSpace(details.Description)) { return details.Description; }
        if (!string.IsNullOrWhiteSpace(details.Name)) { return details.Name; }

        return details.Path ?? None;
    }

    private static string Seconds(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    private void Row(string label, string value) =>
        Shell.WriteLine("  " + (label + ":").PadRight(LabelWidth) + value);
}

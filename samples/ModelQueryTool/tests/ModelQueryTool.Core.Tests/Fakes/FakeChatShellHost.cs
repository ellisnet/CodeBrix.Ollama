using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ModelQueryTool.Services;

namespace ModelQueryTool.Core.Tests.Fakes;

/// <summary>
/// The status bar and the way out, recorded rather than shown. Everything the chat posts is run
/// where it was posted, so a test sees the effect at the moment the chat asked for it.
/// </summary>
internal sealed class FakeChatShellHost : IChatShellHost
{
    private readonly object _gate = new();
    private readonly List<ShellProgressReport> _reports = [];
    private readonly List<string> _copied = [];

    /// <summary>Gets every status-bar change, in the order it was asked for.</summary>
    internal IReadOnlyList<ShellProgressReport> Reports
    {
        get { lock (_gate) { return [.. _reports]; } }
    }

    /// <summary>Gets every piece of text the chat asked to have copied, in order.</summary>
    internal IReadOnlyList<string> Copied
    {
        get { lock (_gate) { return [.. _copied]; } }
    }

    /// <summary>Gets or sets whether a copy succeeds; false stands in for a head with no clipboard.</summary>
    internal bool CopySucceeds { get; set; } = true;

    /// <summary>Gets the last piece of text the chat asked to have copied, or null when there was none.</summary>
    internal string LastCopied
    {
        get
        {
            lock (_gate)
            {
                return _copied.Count == 0 ? null : _copied[_copied.Count - 1];
            }
        }
    }

    /// <summary>Gets how many times the application was asked to close.</summary>
    internal int ExitCalls { get; private set; }

    /// <summary>Gets the latest status-bar change, or null when there has not been one.</summary>
    internal ShellProgressReport Latest
    {
        get
        {
            lock (_gate)
            {
                return _reports.Count == 0 ? null : _reports[_reports.Count - 1];
            }
        }
    }

    /// <summary>Forgets every status-bar change recorded so far.</summary>
    internal void ClearReports()
    {
        lock (_gate) { _reports.Clear(); }
    }

    /// <inheritdoc />
    public void Post(Action action) => action?.Invoke();

    /// <inheritdoc />
    public void ShowProgress(string caption, double percent, bool isIndeterminate)
    {
        lock (_gate)
        {
            _reports.Add(new ShellProgressReport(true, caption, percent, isIndeterminate));
        }
    }

    /// <inheritdoc />
    public void HideProgress()
    {
        lock (_gate)
        {
            _reports.Add(new ShellProgressReport(false, string.Empty, 0d, false));
        }
    }

    /// <inheritdoc />
    public Task<bool> CopyTextAsync(string text)
    {
        lock (_gate) { _copied.Add(text); }

        return Task.FromResult(CopySucceeds);
    }

    /// <inheritdoc />
    public void Exit() => ExitCalls++;
}

/// <summary>One change of the status bar, as the chat asked for it.</summary>
internal sealed class ShellProgressReport
{
    /// <summary>Records the change.</summary>
    /// <param name="isVisible">Whether the bar is showing afterwards.</param>
    /// <param name="caption">The line above the bar.</param>
    /// <param name="percent">How full the bar is.</param>
    /// <param name="isIndeterminate">Whether the bar animates instead of filling.</param>
    internal ShellProgressReport(bool isVisible, string caption, double percent, bool isIndeterminate)
    {
        IsVisible = isVisible;
        Caption = caption;
        Percent = percent;
        IsIndeterminate = isIndeterminate;
    }

    /// <summary>Gets whether the bar is showing afterwards.</summary>
    internal bool IsVisible { get; }

    /// <summary>Gets the line above the bar.</summary>
    internal string Caption { get; }

    /// <summary>Gets how full the bar is, from zero to one hundred.</summary>
    internal double Percent { get; }

    /// <summary>Gets whether the bar animates instead of filling.</summary>
    internal bool IsIndeterminate { get; }
}

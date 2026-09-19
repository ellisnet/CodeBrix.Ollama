using System;
using System.Threading.Tasks;

namespace ModelQueryTool.Services;

/// <summary>
/// The little the chat asks of whatever is hosting it: a way onto the thread that owns the
/// terminal, the status bar under it, and the way out of the application.
/// </summary>
/// <remarks>
/// <para>
/// The chat is a plain class with no user interface of its own, which is what makes it testable
/// without a window. Everything it cannot do by itself arrives through this interface, and the
/// view model that implements it is the only place that knows about dispatchers, bindings and
/// commands.
/// </para>
/// <para>
/// THREADING. <see cref="Post"/> is how the chat reaches the terminal's own thread, and it is
/// the only member with a thread requirement of its own: the action must run on the thread the
/// terminal's input arrives on. The others are called from worker threads, from the download's
/// thread and from the engine's loading thread, so the implementation marshals.
/// </para>
/// </remarks>
public interface IChatShellHost
{
    /// <summary>
    /// Runs an action on the thread that drives the terminal - the one the control raises its
    /// input on - and returns without waiting for it.
    /// </summary>
    /// <param name="action">What to run.</param>
    void Post(Action action);

    /// <summary>Shows the status bar, or updates the one already showing.</summary>
    /// <param name="caption">The line above the bar.</param>
    /// <param name="percent">How far the work has got, from zero to one hundred.</param>
    /// <param name="isIndeterminate">
    /// True while the work cannot say how far it has got, so the bar animates instead of filling.
    /// </param>
    void ShowProgress(string caption, double percent, bool isIndeterminate);

    /// <summary>Hides the status bar, because nothing long is running any more.</summary>
    void HideProgress();

    /// <summary>
    /// Puts text on the system clipboard, exactly as it is given - the clipboard belongs to the
    /// head, not to the chat, so the chat can only ask for it.
    /// </summary>
    /// <param name="text">The text to copy, verbatim.</param>
    /// <returns>
    /// True when the text is on the clipboard; false when this head has no clipboard to reach or
    /// the clipboard refused the text, which the chat then says in one line.
    /// </returns>
    Task<bool> CopyTextAsync(string text);

    /// <summary>Closes the application.</summary>
    void Exit();
}

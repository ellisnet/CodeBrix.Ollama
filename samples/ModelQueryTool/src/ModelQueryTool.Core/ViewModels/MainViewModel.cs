using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml;
using ModelQueryTool.ModelAccess;
using ModelQueryTool.ModelRunning;
using ModelQueryTool.Services;
using System;
using System.Threading.Tasks;

namespace ModelQueryTool.ViewModels;

/// <summary>
/// The thin binder between the page and the chat. It owns nothing of the chat's behaviour: it
/// resolves the two model services, builds the <see cref="ChatShell"/> over them, forwards the
/// terminal's three wires, and marshals everything the chat wants shown onto the thread the
/// bindings live on.
/// </summary>
/// <remarks>
/// The clipboard is the one thing the chat asks for that no shared code can do, so it arrives as
/// a bridge the page fills in (<see cref="IClipboardBridge"/>); with no bridge the view model
/// answers that the copy could not be made, which is what a head with no clipboard needs.
/// </remarks>
[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, IChatShellHost, IClipboardBridge
{
    private readonly ChatShell _shell;

    /// <summary>Builds the view model and, with it, the chat.</summary>
    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        //The chat writes through the delegate the page fills in on the terminal's Loaded; until
        //  then there is nothing to write to, and the control would drop anything fed to it.
        _shell = new ChatShell(
            GetService<IModelStager>(),
            GetService<IModelHost>(),
            text => FeedToTerminal?.Invoke(text),
            this);
    }

    #region | Bindable properties |

    /// <summary>Gets whether a download, a load or a turn is running, which is what shows the status bar.</summary>
    [AffectsProperties(nameof(StatusBarVisibility))]
    public bool IsWorking
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Gets the status bar's visibility: it is there only while something long is running.</summary>
    public Visibility StatusBarVisibility => GetVisibility(IsWorking);

    /// <summary>Gets the line above the status bar - what is happening, and how far it has come.</summary>
    public string StatusCaption
    {
        get;
        private set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>Gets how full the status bar is, from zero to one hundred.</summary>
    public double StatusProgress
    {
        get;
        private set
        {
            //No SetProperty overload takes a double; compare-and-notify by hand.
            if (field.Equals(value)) { return; }
            field = value;
            NotifyPropertyChanged(nameof(StatusProgress));
        }
    }

    /// <summary>
    /// Gets whether the status bar animates instead of filling, which is what a wait with no
    /// measurable end - the model reading the conversation back - looks like.
    /// </summary>
    public bool IsStatusIndeterminate
    {
        get;
        private set => SetProperty(ref field, value);
    }

    #endregion

    #region | Commands and their implementations |

    /// <summary>Gets the command behind the Cancel button: it stops whatever long thing is running.</summary>
    public SimpleCommand CancelCommand => field ??= new SimpleCommand(DoCancel, executeOnMainThread: true);

    private void DoCancel() => _shell?.Cancel();

    #endregion

    #region | Terminal bridge - wired by the page's code-behind |

    /// <summary>Gets or sets the terminal control's feed, which the page hands over once the control is on screen.</summary>
    public Action<string> FeedToTerminal { get; set; }

    /// <summary>Routes one chunk of the terminal's keyboard input into the chat.</summary>
    /// <param name="data">The chunk, exactly as the control delivered it.</param>
    public void OnTerminalInput(string data) => _shell?.SendInput(data);

    /// <summary>Tells the chat how big the terminal's grid is, at the start and after every resize.</summary>
    /// <param name="columns">The terminal's column count.</param>
    /// <param name="rows">The terminal's row count.</param>
    public void OnTerminalResized(int columns, int rows) => _shell?.SetGridSize(columns, rows);

    /// <summary>Starts the chat, now that there is a terminal for it to write to.</summary>
    public void OnTerminalReady() => _shell?.Start();

    #endregion

    #region | Head capabilities - wired by the page's code-behind |

    /// <inheritdoc />
    public Func<string, bool> CopyTextToClipboard { get; set; }

    #endregion

    #region | What the chat asks of its host |

    /// <inheritdoc />
    public void Post(Action action) => InvokeOnMainThread(action);

    /// <inheritdoc />
    public void ShowProgress(string caption, double percent, bool isIndeterminate) =>
        InvokeOnMainThread(() =>
        {
            StatusCaption = caption ?? string.Empty;
            StatusProgress = percent;
            IsStatusIndeterminate = isIndeterminate;
            IsWorking = true;
        });

    /// <inheritdoc />
    public void HideProgress() =>
        InvokeOnMainThread(() =>
        {
            IsWorking = false;
            IsStatusIndeterminate = false;
            StatusCaption = string.Empty;
            StatusProgress = 0d;
        });

    /// <inheritdoc />
    public Task<bool> CopyTextAsync(string text)
    {
        var copy = CopyTextToClipboard;

        if (copy == null || string.IsNullOrEmpty(text)) { return Task.FromResult(false); }

        //The clipboard belongs to the user interface, and the chat asks for it from a worker
        //  thread, so the head's own copy is run where the bindings live.
        return InvokeOnMainThreadAsync(() => Task.FromResult(copy(text)));
    }

    /// <inheritdoc />
    public void Exit() => InvokeOnMainThread(() => Application.Current.Exit());

    #endregion
}

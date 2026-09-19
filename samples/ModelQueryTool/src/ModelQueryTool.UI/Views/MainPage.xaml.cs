using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml.Controls;
using ModelQueryTool.ViewModels;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace ModelQueryTool.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        DataContextChanged += (_, _) =>
        {
            //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);

            //The clipboard is a head capability, so the page supplies it; a head without one
            //  simply leaves the bridge alone and /copy says the copy could not be made
            if (DataContext is IClipboardBridge clipboard)
            {
                clipboard.CopyTextToClipboard = CopyToClipboard;
            }
        };

        this.InitializeComponent(); //Leave this line last

        //The terminal's three wires: keys in, grid size in, text out. Nothing may be written
        //  before Loaded - the control has no dispatcher queue until then and drops what it is fed.
        Terminal.InputEmitted += data => ViewModel?.OnTerminalInput(data);
        Terminal.GridResized += (columns, rows) => ViewModel?.OnTerminalResized(columns, rows);
        Terminal.Loaded += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.FeedToTerminal = Terminal.Feed;
                viewModel.OnTerminalResized(Terminal.Columns, Terminal.Rows);
                viewModel.OnTerminalReady();
            }

            Terminal.GrabFocus();
        };
    }

    private MainViewModel ViewModel => DataContext as MainViewModel;

    private static bool CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);

            //This framework's clipboard contract: set the content, then flush it, so that what
            //  was copied outlives the application
            Clipboard.SetContent(package);
            Clipboard.Flush();

            return true;
        }
        catch (Exception)
        {
            //The clipboard can be unavailable for a moment; the copy simply does not take, and
            //  the chat says so rather than claiming the text was copied
            return false;
        }
    }
}

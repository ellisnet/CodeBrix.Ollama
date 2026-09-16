using CodeBrix.Platform.Simple;
using Microsoft.UI.Xaml.Controls;

namespace ModelQueryTool.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        DataContextChanged += (_, _) =>
        {
            //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);
        };

        this.InitializeComponent(); //Leave this line last
    }
}

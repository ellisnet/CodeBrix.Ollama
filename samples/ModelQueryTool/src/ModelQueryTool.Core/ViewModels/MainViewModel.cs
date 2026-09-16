using CodeBrix.Platform.Simple;
using System.Diagnostics;

namespace ModelQueryTool.ViewModels;

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel
{
    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");
    }

    #region | Bindable properties |

    public string Greeting => "Hello from ModelQueryTool!";

    #endregion

    #region | Commands and their implementations |

    //No commands yet...

    #endregion
}

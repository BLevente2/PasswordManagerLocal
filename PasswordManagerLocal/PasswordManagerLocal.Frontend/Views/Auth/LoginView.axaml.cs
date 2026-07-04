using Avalonia.Controls;
using PasswordManagerLocal.Frontend.Views.Styling;

namespace PasswordManagerLocal.Frontend.Views.Auth;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        ScrollViewerStyleHelper.ApplyComfortableScrollerStyle(PageScrollViewer);
    }
}

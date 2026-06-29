using Avalonia.Controls;
using PasswordManagerLocal.Views.Styling;

namespace PasswordManagerLocal.Views.Auth;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        ScrollViewerStyleHelper.ApplyComfortableScrollerStyle(PageScrollViewer);
    }
}

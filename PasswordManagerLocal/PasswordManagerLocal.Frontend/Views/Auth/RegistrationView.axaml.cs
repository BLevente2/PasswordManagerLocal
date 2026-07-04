using Avalonia.Controls;
using PasswordManagerLocal.Frontend.Views.Styling;

namespace PasswordManagerLocal.Frontend.Views.Auth;

public partial class RegistrationView : UserControl
{
    public RegistrationView()
    {
        InitializeComponent();
        ScrollViewerStyleHelper.ApplyComfortableScrollerStyle(PageScrollViewer);
    }
}

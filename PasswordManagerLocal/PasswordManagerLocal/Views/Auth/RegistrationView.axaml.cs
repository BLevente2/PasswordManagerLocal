using Avalonia.Controls;
using PasswordManagerLocal.Views.Styling;

namespace PasswordManagerLocal.Views.Auth;

public partial class RegistrationView : UserControl
{
    public RegistrationView()
    {
        InitializeComponent();
        ScrollViewerStyleHelper.ApplyComfortableScrollerStyle(PageScrollViewer);
    }
}

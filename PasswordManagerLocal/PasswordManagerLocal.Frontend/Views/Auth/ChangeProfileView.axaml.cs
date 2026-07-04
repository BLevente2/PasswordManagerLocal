using Avalonia.Controls;
using PasswordManagerLocal.Frontend.Views.Styling;

namespace PasswordManagerLocal.Frontend.Views.Auth;

public partial class ChangeProfileView : UserControl
{
    public ChangeProfileView()
    {
        InitializeComponent();
        ScrollViewerStyleHelper.ApplyComfortableScrollerStyle(PageScrollViewer);
    }
}

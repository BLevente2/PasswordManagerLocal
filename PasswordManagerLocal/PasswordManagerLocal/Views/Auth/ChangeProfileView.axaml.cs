using Avalonia.Controls;
using PasswordManagerLocal.Views.Styling;

namespace PasswordManagerLocal.Views.Auth;

public partial class ChangeProfileView : UserControl
{
    public ChangeProfileView()
    {
        InitializeComponent();
        ScrollViewerStyleHelper.ApplyComfortableScrollerStyle(PageScrollViewer);
    }
}

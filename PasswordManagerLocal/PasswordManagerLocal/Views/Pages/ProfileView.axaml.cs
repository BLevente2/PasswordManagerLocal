using Avalonia.Controls;
using PasswordManagerLocal.Views.Styling;

namespace PasswordManagerLocal.Views.Pages;

public partial class ProfileView : UserControl
{
    public ProfileView()
    {
        InitializeComponent();
        ScrollViewerStyleHelper.ApplyComfortableScrollerStyle(ProfileMainScrollViewer);
    }
}

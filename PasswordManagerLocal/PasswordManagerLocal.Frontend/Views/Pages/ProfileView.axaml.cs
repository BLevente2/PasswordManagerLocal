using Avalonia.Controls;
using PasswordManagerLocal.Frontend.Views.Styling;

namespace PasswordManagerLocal.Frontend.Views.Pages;

public partial class ProfileView : UserControl
{
    public ProfileView()
    {
        InitializeComponent();
        ScrollViewerStyleHelper.ApplyComfortableScrollerStyle(ProfileMainScrollViewer);
    }
}

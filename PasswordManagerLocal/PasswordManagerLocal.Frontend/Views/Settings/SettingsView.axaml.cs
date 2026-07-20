using Avalonia.Controls;
using PasswordManagerLocal.Frontend.Views.Styling;

namespace PasswordManagerLocal.Frontend.Views.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        ScrollViewerStyleHelper.ApplyComfortableScrollerStyle(PageScrollViewer);
    }
}

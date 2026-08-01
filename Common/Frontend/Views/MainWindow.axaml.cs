using Avalonia.Controls;
using PasswordManagerLocal.Common.Frontend.Services;

namespace PasswordManagerLocal.Common.Frontend.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            ClipboardService.SetActiveTopLevel(this);
            QrImagePickerService.SetActiveTopLevel(this);
        };
        Deactivated += (_, _) => SensitiveDataVisibilityService.RequestHideVisibleSecrets();
    }
}

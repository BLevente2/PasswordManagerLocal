using Avalonia.Controls;
using PasswordManagerLocal.Frontend.Services;

namespace PasswordManagerLocal.Frontend.Views;

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

using Avalonia.Controls;
using PasswordManagerLocal.Services;

namespace PasswordManagerLocal.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ClipboardService.SetActiveTopLevel(this);
    }
}

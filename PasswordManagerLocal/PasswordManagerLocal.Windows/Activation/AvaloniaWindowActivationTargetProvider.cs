using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace PasswordManagerLocal.Windows.Activation;

public sealed class AvaloniaWindowActivationTargetProvider : IWindowsWindowActivationTargetProvider
{
    public IWindowsWindowActivationTarget? GetTarget()
    {
        if (Application.Current?.ApplicationLifetime is not
            IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is not { } window)
        {
            return null;
        }

        return new AvaloniaWindowActivationTarget(window);
    }
}

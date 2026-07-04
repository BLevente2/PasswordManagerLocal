using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Frontend.Abstractions.Services;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Frontend.ViewModels;
using PasswordManagerLocal.Frontend.Views;
using PasswordManagerLocal.Backend;
using PasswordManagerLocal.Backend.Abstractions.Services;

namespace PasswordManagerLocal.Frontend;

public partial class App : Application
{
    public static IAuthSessionRegistry AuthSessionRegistry { get; } = new AuthSessionRegistry();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var endpoints = new DeferredEndpoints();
        var mainViewModel = new MainViewModel(endpoints);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            if (OperatingSystem.IsWindows())
                mainWindow.WindowState = WindowState.Maximized;

            desktop.MainWindow = mainWindow;

            Dispatcher.UIThread.Post(async () =>
            {
                await mainViewModel.InitializeAsync();
                await TryShowFirewallPermissionPromptAsync(mainWindow, mainViewModel);
            }, DispatcherPriority.Background);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = mainViewModel
            };

            Dispatcher.UIThread.Post(
                async () => await mainViewModel.InitializeAsync(),
                DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }



    private static async Task TryShowFirewallPermissionPromptAsync(MainWindow mainWindow, MainViewModel mainViewModel)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            await BackendHost.WaitUntilInitializedAsync();

            var identity = BackendHost.Services.GetRequiredService<IDeviceIdentityService>();
            if (!identity.IsSyncOn)
                return;

            await FirewallPermissionStartupPrompt.TryShowAsync(mainWindow, mainViewModel.CurrentLanguage);
        }
        catch
        {
        }
    }
}

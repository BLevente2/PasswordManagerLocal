using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Abstractions.Services;
using PasswordManagerLocal.Services;
using PasswordManagerLocal.ViewModels;
using PasswordManagerLocal.Views;
using PasswordManagerLocalBackend;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Abstractions.Services;

namespace PasswordManagerLocal;

public partial class App : Application
{
    public static IAuthSessionRegistry AuthSessionRegistry { get; } = new AuthSessionRegistry();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        await BackendHost.InitializeAsync();

        var endpoints = BackendHost.Services.GetRequiredService<IEndpoints>();
        var mainViewModel = new MainViewModel(endpoints);
        await mainViewModel.InitializeAsync();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            if (OperatingSystem.IsWindows())
                mainWindow.WindowState = WindowState.Maximized;

            desktop.MainWindow = mainWindow;

            Dispatcher.UIThread.Post(async () => await TryShowFirewallPermissionPromptAsync(mainWindow, mainViewModel));
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }



    private static async Task TryShowFirewallPermissionPromptAsync(MainWindow mainWindow, MainViewModel mainViewModel)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
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

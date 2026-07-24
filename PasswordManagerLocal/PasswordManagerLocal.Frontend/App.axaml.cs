using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Frontend.Abstractions.Services;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Frontend.ViewModels;
using PasswordManagerLocal.Frontend.Views;

namespace PasswordManagerLocal.Frontend;

public partial class App : Application
{
    private readonly FrontendApplicationContext _context;

    public App(FrontendApplicationContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        AppConfigurationManager.Initialize(context.ApplicationDataDirectory);
    }

    public static IAuthSessionRegistry AuthSessionRegistry { get; } = new AuthSessionRegistry();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var endpoints = new DeferredEndpoints(_context.BackendClient);
        var mainViewModel = new MainViewModel(
            endpoints,
            _context.BackendClient,
            _context.BackgroundSyncSettingsClient);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            if (OperatingSystem.IsWindows())
                mainWindow.WindowState = WindowState.Maximized;

            mainWindow.Closed += (_, _) =>
            {
                _context.DesktopExitRequested?.Invoke();
                TryShutdownDesktop(desktop);
            };
            desktop.MainWindow = mainWindow;

            Dispatcher.UIThread.Post(async () =>
            {
                await mainViewModel.InitializeAsync();
                await TryShowFirewallPermissionPromptAsync(mainWindow, mainViewModel, endpoints);
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

    private static void TryShutdownDesktop(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            desktop.TryShutdown();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task TryShowFirewallPermissionPromptAsync(
        MainWindow mainWindow,
        MainViewModel mainViewModel,
        IEndpoints endpoints)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            await _context.BackendClient.WaitUntilReadyAsync();

            var localDevice = await endpoints.GetLocalDeviceInfoAsync();
            if (!localDevice.IsSyncOn)
                return;

            await FirewallPermissionStartupPrompt.TryShowAsync(mainWindow, mainViewModel.CurrentLanguage);
        }
        catch
        {
        }
    }
}

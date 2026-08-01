using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PasswordManagerLocal.Common.Contracts.Endpoints;
using PasswordManagerLocal.Common.Frontend.Abstractions.Services;
using PasswordManagerLocal.Common.Frontend.Services;
using PasswordManagerLocal.Common.Frontend.ViewModels;
using PasswordManagerLocal.Common.Frontend.Views;

namespace PasswordManagerLocal.Common.Frontend;

public partial class App : Application
{
    private readonly FrontendApplicationContext? _context;

    // Required by Avalonia's XAML resource loader and design-time tooling.
    // Production hosts construct App through the explicit context factory below.
    public App()
    {
    }

    public App(FrontendApplicationContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        if (OperatingSystem.IsWindows())
            WindowsFirewallConfigurationStore.Initialize(context.ApplicationDataDirectory);
    }

    private FrontendApplicationContext Context => _context ?? throw new InvalidOperationException(
        "The frontend application context was not supplied by the platform host.");

    public static IAuthSessionRegistry AuthSessionRegistry { get; } = new AuthSessionRegistry();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var context = Context;
        var endpoints = new DeferredEndpoints(context.BackendClient);
        var mainViewModel = new MainViewModel(
            endpoints,
            context.BackendClient,
            context.BackgroundSyncSettingsClient,
            context.ApplicationPreferencesStore);

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
                context.DesktopExitRequested?.Invoke();
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
            await Context.BackendClient.WaitUntilReadyAsync();

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

using Avalonia;
using Avalonia.ReactiveUI;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Backend.Windows;
using PasswordManagerLocal.Frontend;
using PasswordManagerLocal.Frontend.Services;
using System;

namespace PasswordManagerLocal.Windows;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ClipboardService.SetPlatformClipboardWriter(new WindowsClipboardWriter());
        FirewallPermissionService.SetPlatformFirewallPermissionManager(new WindowsFirewallPermissionManager());

        var composition = WindowsBackendRuntimeFactory.Create();
        var backendClient = new InProcessFrontendBackendClient(composition.Runtime);
        var backgroundSyncSettingsStore = new FileBackgroundSyncSettingsStore(
            composition.ApplicationDataDirectory);
        var frontendContext = new FrontendApplicationContext(
            backendClient,
            backgroundSyncSettingsStore,
            composition.ApplicationDataDirectory);

        try
        {
            BuildAvaloniaApp(frontendContext)
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            backendClient
                .DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
            composition.Runtime
                .DisposeAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
    }

    public static AppBuilder BuildAvaloniaApp(FrontendApplicationContext frontendContext)
    {
        ArgumentNullException.ThrowIfNull(frontendContext);

        var builder = AppBuilder.Configure(() => new App(frontendContext))
            .UseWin32()
            .UseSkia()
            .WithInterFont()
            .UseReactiveUI();

#if DEBUG
        builder = builder.LogToTrace();
#endif

        return builder;
    }
}

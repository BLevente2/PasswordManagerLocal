using Avalonia;
using Avalonia.ReactiveUI;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend;
using System;

namespace PasswordManagerLocal.Windows;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ClipboardService.SetPlatformClipboardWriter(new WindowsClipboardWriter());
        FirewallPermissionService.SetPlatformFirewallPermissionManager(new WindowsFirewallPermissionManager());
        _ = BackendHost.StartInitializationAsync(new DpapiKeyProtector());

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .UseReactiveUI()
            .LogToTrace();
}
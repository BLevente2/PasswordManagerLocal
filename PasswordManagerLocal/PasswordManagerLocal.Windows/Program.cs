using Avalonia;
using Avalonia.ReactiveUI;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend;
using System;
using System.Threading.Tasks;

namespace PasswordManagerLocal.Windows;

internal sealed class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        ClipboardService.SetPlatformClipboardWriter(new WindowsClipboardWriter());
        FirewallPermissionService.SetPlatformFirewallPermissionManager(new WindowsFirewallPermissionManager());
        await BackendHost.InitializeAsync(new DpapiKeyProtector());

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
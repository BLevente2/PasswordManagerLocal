using Avalonia;
using Avalonia.ReactiveUI;
using PasswordManagerLocal.Frontend;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Backend;
using System;

namespace PasswordManagerLocal.Windows;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ClipboardService.SetPlatformClipboardWriter(new WindowsClipboardWriter());
        FirewallPermissionService.SetPlatformFirewallPermissionManager(new WindowsFirewallPermissionManager());
        BackendHost.ConfigurePlatformKeyProtector(new DpapiKeyProtector());

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
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
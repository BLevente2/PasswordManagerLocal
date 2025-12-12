using Avalonia;
using Avalonia.ReactiveUI;
using PasswordManagerLocalBackend;
using System;

namespace PasswordManagerLocal.Windows;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BackendHost.Initialize(new DpapiKeyProtector());

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
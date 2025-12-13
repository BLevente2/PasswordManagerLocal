using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Abstractions.Services;
using PasswordManagerLocal.Services;
using PasswordManagerLocal.ViewModels;
using PasswordManagerLocal.Views;
using PasswordManagerLocalBackend;
using PasswordManagerLocalBackend.Abstractions;

namespace PasswordManagerLocal
{
    public partial class App : Application
    {
        public static IAuthSessionRegistry AuthSessionRegistry { get; } = new AuthSessionRegistry();

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            BackendHost.Initialize();

            var backendAPI = BackendHost.Services.GetRequiredService<IEndpoints>();
            var mainViewModel = new MainViewModel(backendAPI);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };
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
    }
}
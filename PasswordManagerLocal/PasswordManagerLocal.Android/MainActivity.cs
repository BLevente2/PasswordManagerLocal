using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Avalonia.ReactiveUI;
using PasswordManagerLocal.Services;
using PasswordManagerLocalBackend;

namespace PasswordManagerLocal.Android;

[Activity(
Label = "PasswordManagerLocal.Android",
Theme = "@style/MyTheme.NoActionBar",
Icon = "@drawable/icon",
MainLauncher = true,
ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        ClipboardService.SetPlatformClipboardWriter(new AndroidClipboardWriter(this));
        BackendHost.InitializeAsync(new AndroidKeyProtector()).GetAwaiter().GetResult();

        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .UseReactiveUI();
    }
}
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
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
    private WifiManager.MulticastLock? _multicastLock;


    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        ClipboardService.SetPlatformClipboardWriter(new AndroidClipboardWriter(this));
        AcquireMulticastLock();
        BackendHost.InitializeAsync(new AndroidKeyProtector()).GetAwaiter().GetResult();

        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .UseReactiveUI();
    }


    protected override void OnDestroy()
    {
        ReleaseMulticastLock();
        base.OnDestroy();
    }


    private void AcquireMulticastLock()
    {
        try
        {
            var wifiManager = ApplicationContext?.GetSystemService(Context.WifiService) as WifiManager;
            _multicastLock = wifiManager?.CreateMulticastLock("PasswordManagerLocal.Mdns");
            _multicastLock?.SetReferenceCounted(false);

            if (_multicastLock is not null && !_multicastLock.IsHeld)
                _multicastLock.Acquire();
        }
        catch
        {
            _multicastLock = null;
        }
    }


    private void ReleaseMulticastLock()
    {
        try
        {
            if (_multicastLock is not null && _multicastLock.IsHeld)
                _multicastLock.Release();
        }
        catch
        {
        }
        finally
        {
            _multicastLock = null;
        }
    }
}

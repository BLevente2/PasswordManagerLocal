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
    private const int EnrollmentQrScannerRequestCode = 7301;

    private WifiManager.MulticastLock? _multicastLock;
    private TaskCompletionSource<string?>? _enrollmentQrScanCompletion;
    private CancellationTokenRegistration _enrollmentQrScanCancellationRegistration;


    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        global::PasswordManagerLocal.Services.ClipboardService.SetPlatformClipboardWriter(new AndroidClipboardWriter(this));
        EnrollmentQrCodeCameraScannerService.SetPlatformScanner(new AndroidQrCodeCameraScanner(this));
        AcquireMulticastLock();
        _ = BackendHost.StartInitializationAsync(new AndroidKeyProtector());

        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .UseReactiveUI();
    }


    protected override void OnPause()
    {
        SensitiveDataVisibilityService.RequestHideVisibleSecrets();
        base.OnPause();
    }


    protected override void OnStop()
    {
        SensitiveDataVisibilityService.RequestHideVisibleSecrets();
        base.OnStop();
    }


    protected override void OnDestroy()
    {
        CompleteEnrollmentQrScan(null);
        EnrollmentQrCodeCameraScannerService.SetPlatformScanner(null);
        ReleaseMulticastLock();
        base.OnDestroy();
    }


    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == EnrollmentQrScannerRequestCode)
        {
            var enrollmentCode = resultCode == Result.Ok
                ? data?.GetStringExtra(EnrollmentQrScannerActivity.ResultEnrollmentCodeExtra)
                : null;

            CompleteEnrollmentQrScan(enrollmentCode);
            return;
        }

        base.OnActivityResult(requestCode, resultCode, data);
    }


    internal Task<string?> ScanEnrollmentQrCodeAsync(
        string? title = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (_enrollmentQrScanCompletion is not null)
            return Task.FromResult<string?>(null);

        try
        {
            _enrollmentQrScanCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (cancellationToken.CanBeCanceled)
                _enrollmentQrScanCancellationRegistration = cancellationToken.Register(() => CompleteEnrollmentQrScan(null));

            var intent = new Intent(this, typeof(EnrollmentQrScannerActivity));
            if (!string.IsNullOrWhiteSpace(title))
                intent.PutExtra(EnrollmentQrScannerActivity.TitleExtra, title);

            if (!string.IsNullOrWhiteSpace(description))
                intent.PutExtra(EnrollmentQrScannerActivity.DescriptionExtra, description);

            StartActivityForResult(intent, EnrollmentQrScannerRequestCode);
            return _enrollmentQrScanCompletion.Task;
        }
        catch
        {
            CompleteEnrollmentQrScan(null);
            return Task.FromResult<string?>(null);
        }
    }


    private void CompleteEnrollmentQrScan(string? enrollmentCode)
    {
        var completion = _enrollmentQrScanCompletion;

        _enrollmentQrScanCompletion = null;
        _enrollmentQrScanCancellationRegistration.Dispose();
        _enrollmentQrScanCancellationRegistration = default;

        completion?.TrySetResult(enrollmentCode);
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

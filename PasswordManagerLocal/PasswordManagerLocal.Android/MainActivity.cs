using System.Diagnostics;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Views.InputMethods;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using PasswordManagerLocal.Frontend;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Frontend.ViewModels;
using PasswordManagerLocal.Frontend.Views;
using PasswordManagerLocal.Backend.Hosting;

namespace PasswordManagerLocal.Android;

[Activity(
Label = "PasswordManagerLocal.Android",
Theme = "@style/MyTheme.NoActionBar",
Icon = "@drawable/icon",
MainLauncher = true,
ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private const int EnrollmentQrScannerRequestCode = 7301;
    private static readonly TimeSpan DuplicateBackRequestSuppressionWindow = TimeSpan.FromMilliseconds(250);

    private TaskCompletionSource<string?>? _enrollmentQrScanCompletion;
    private CancellationTokenRegistration _enrollmentQrScanCancellationRegistration;
    private bool _isHandlingBackRequest;
    private long _lastAcceptedBackRequestTimestamp;
    private AlertDialog? _backConfirmationDialog;


    protected override AppBuilder CreateAppBuilder()
    {
        var application = Application as PasswordManagerLocalApplication
            ?? throw new InvalidOperationException("The process-level application composition root is unavailable.");
        var frontendContext = new FrontendApplicationContext(
            application.BackendRuntime,
            application.ApplicationDataDirectory);

        return AppBuilder.Configure(() => new App(frontendContext))
            .UseAndroid();
    }


    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        global::PasswordManagerLocal.Frontend.Services.ClipboardService.SetPlatformClipboardWriter(new AndroidClipboardWriter(this));
        SoftwareKeyboardService.SetPlatformHideAction(HideSoftwareKeyboard);
        EnrollmentQrCodeCameraScannerService.SetPlatformScanner(new AndroidQrCodeCameraScanner(this));

        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .UseReactiveUI();
    }


    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BackRequested += HandleBackRequested;
    }


    protected override void OnResume()
    {
        base.OnResume();

        if (Application is PasswordManagerLocalApplication application)
            _ = ResumeBackendAsync(application.BackendRuntime);
    }


    private static async Task ResumeBackendAsync(IBackendRuntime backendRuntime)
    {
        if (backendRuntime.Snapshot.State == BackendRuntimeState.WaitingForDeviceUnlock)
        {
            try
            {
                await backendRuntime.EnsureStartedAsync();
            }
            catch
            {
            }
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView &&
            singleView.MainView?.DataContext is MainViewModel viewModel &&
            !viewModel.IsAuthenticated)
        {
            try
            {
                await viewModel.InitializeAsync();
            }
            catch
            {
            }
        }
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
        BackRequested -= HandleBackRequested;
        _backConfirmationDialog?.Dismiss();
        _backConfirmationDialog?.Dispose();
        _backConfirmationDialog = null;
        CompleteEnrollmentQrScan(null);
        EnrollmentQrCodeCameraScannerService.SetPlatformScanner(null);
        SoftwareKeyboardService.SetPlatformHideAction(null);
        base.OnDestroy();
    }


    private async void HandleBackRequested(object? sender, AndroidBackRequestedEventArgs e)
    {
        // Always consume Android's native back request. App-level back navigation is
        // handled by MainView through the same path used by the Windows Esc key.
        e.Handled = true;

        // Avalonia/Android can deliver two BackRequested notifications for one physical
        // back action on some Android versions or emulator configurations. The existing
        // in-progress flag only blocks overlapping async calls; a fast first navigation
        // can finish before the duplicate notification arrives. Suppress only near-
        // simultaneous Android duplicates while preserving intentional repeated presses.
        var backRequestTimestamp = Stopwatch.GetTimestamp();
        if (_lastAcceptedBackRequestTimestamp != 0
            && Stopwatch.GetElapsedTime(_lastAcceptedBackRequestTimestamp, backRequestTimestamp)
                < DuplicateBackRequestSuppressionWindow)
        {
            return;
        }

        _lastAcceptedBackRequestTimestamp = backRequestTimestamp;

        if (_backConfirmationDialog is { IsShowing: true } dialog)
        {
            _backConfirmationDialog = null;
            dialog.Dismiss();
            dialog.Dispose();
            return;
        }

        if (_isHandlingBackRequest
            || Avalonia.Application.Current?.ApplicationLifetime is not ISingleViewApplicationLifetime singleView
            || singleView.MainView is not MainView mainView)
        {
            return;
        }

        _isHandlingBackRequest = true;

        try
        {
            var wasHandled = await mainView.HandleBackRequestAsync();
            if (!wasHandled && mainView.DataContext is MainViewModel viewModel)
                ShowRootBackConfirmation(viewModel);
        }
        finally
        {
            _isHandlingBackRequest = false;
        }
    }


    private void ShowRootBackConfirmation(MainViewModel viewModel)
    {
        if (_backConfirmationDialog?.IsShowing == true)
            return;

        _backConfirmationDialog?.Dispose();
        _backConfirmationDialog = null;

        var shouldLogout = viewModel.IsAuthenticated;
        var builder = new AlertDialog.Builder(this)
            .SetTitle(shouldLogout ? viewModel.LogoutConfirmationTitle : viewModel.ExitConfirmationTitle)
            .SetMessage(shouldLogout ? viewModel.LogoutConfirmationMessage : viewModel.ExitConfirmationMessage)
            .SetNegativeButton(viewModel.NoLabel, (_, _) => _backConfirmationDialog = null)
            .SetPositiveButton(viewModel.YesLabel, async (_, _) =>
            {
                _backConfirmationDialog = null;

                if (shouldLogout)
                    await viewModel.RequestLogoutAsync();
                else
                    FinishAffinity();
            });

        _backConfirmationDialog = builder.Create();
        _backConfirmationDialog.Show();
    }


    private void HideSoftwareKeyboard()
    {
        var inputMethodManager = GetSystemService(InputMethodService) as InputMethodManager;
        var windowToken = CurrentFocus?.WindowToken ?? Window?.DecorView?.WindowToken;
        if (windowToken is not null)
            inputMethodManager?.HideSoftInputFromWindow(windowToken, HideSoftInputFlags.None);
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

}

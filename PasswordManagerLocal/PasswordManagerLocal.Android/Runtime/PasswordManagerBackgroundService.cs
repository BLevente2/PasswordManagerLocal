using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using PasswordManagerLocal.Android.Runtime;
using PasswordManagerLocal.Backend.Android;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Hosting;

namespace PasswordManagerLocal.Android;

[Service(
    Name = "com.levibaba.passwordmanagerlocal.PasswordManagerBackgroundService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class PasswordManagerBackgroundService : Service, IAndroidRuntimeCompositionFactory
{
    private AndroidRuntimeServiceHost? _runtimeHost;
    private AndroidForegroundServiceController? _foregroundController;
    private AndroidSecureStorageAvailability? _secureStorageAvailability;
    private AndroidUserUnlockedReceiver? _userUnlockedReceiver;
    private PasswordManagerBackgroundServiceBinder? _binder;

    public override void OnCreate()
    {
        base.OnCreate();
        var applicationContext = ApplicationContext
            ?? throw new InvalidOperationException("The Android application context is unavailable.");
        var filesDirectory = applicationContext.FilesDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(filesDirectory))
            throw new InvalidOperationException("The Android application-data directory is unavailable.");

        var applicationDataDirectory = Path.Combine(
            filesDirectory,
            ApplicationFileNames.AppFolderName);
        _foregroundController = new AndroidForegroundServiceController(this);
        _secureStorageAvailability = new AndroidSecureStorageAvailability(applicationContext);
        _runtimeHost = new AndroidRuntimeServiceHost(
            this,
            new FileBackgroundSyncSettingsStore(applicationDataDirectory),
            _foregroundController,
            _secureStorageAvailability);
        _binder = new PasswordManagerBackgroundServiceBinder(this);
        RegisterUserUnlockedReceiver();
    }

    public override IBinder OnBind(Intent? intent) =>
        _binder ?? throw new InvalidOperationException("The Android runtime-service binder is unavailable.");

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        try
        {
            _foregroundController?.MarkServiceStarted();
            var secureStorageAvailable = _secureStorageAvailability?.IsAvailable == true;
            _foregroundController?.EnterForeground(
                secureStorageAvailable
                    ? AndroidForegroundNotificationState.BackgroundSynchronizationActive
                    : AndroidForegroundNotificationState.WaitingForDeviceUnlock);
            if (secureStorageAvailable)
                _ = RestoreBackgroundStateAsync();

            return StartCommandResult.Sticky;
        }
        catch
        {
            _foregroundController?.ExitForeground();
            _foregroundController?.RequestStop();
            return StartCommandResult.NotSticky;
        }
    }


    BackendRuntimeComposition IAndroidRuntimeCompositionFactory.Create()
    {
        var applicationContext = ApplicationContext
            ?? throw new InvalidOperationException("The Android application context is unavailable.");
        return AndroidBackendRuntimeFactory.Create(applicationContext);
    }

    public Task<AndroidServiceFrontendBackendClient> AttachInteractiveClientAsync(
        CancellationToken cancellationToken = default) =>
        GetRuntimeHost().AttachInteractiveClientAsync(cancellationToken);

    public Task<AndroidBackgroundSyncServiceState> GetBackgroundStateAsync(
        CancellationToken cancellationToken = default) =>
        GetRuntimeHost().GetBackgroundStateAsync(cancellationToken);

    public Task<AndroidBackgroundSyncServiceState> SetBackgroundEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default) =>
        GetRuntimeHost().SetBackgroundEnabledAsync(isEnabled, cancellationToken);

    public override void OnDestroy()
    {
        var receiver = Interlocked.Exchange(ref _userUnlockedReceiver, null);
        if (receiver is not null)
        {
            try
            {
                UnregisterReceiver(receiver);
            }
            catch (Java.Lang.IllegalArgumentException)
            {
            }
            finally
            {
                receiver.Dispose();
            }
        }

        var host = Interlocked.Exchange(ref _runtimeHost, null);
        try
        {
            if (host is not null)
            {
                Task.Run(async () => await host.DisposeAsync())
                    .GetAwaiter()
                    .GetResult();
            }
        }
        catch
        {
        }
        finally
        {
            _foregroundController?.ExitForeground();
            _foregroundController = null;
            _secureStorageAvailability = null;
            _binder?.Dispose();
            _binder = null;
            base.OnDestroy();
        }
    }


    private void RegisterUserUnlockedReceiver()
    {
        if (_userUnlockedReceiver is not null)
            return;

        var receiver = new AndroidUserUnlockedReceiver(RestoreBackgroundStateAsync);
        var filter = new IntentFilter(Intent.ActionUserUnlocked);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        else
            RegisterReceiver(receiver, filter);

        _userUnlockedReceiver = receiver;
    }

    private async Task RestoreBackgroundStateAsync()
    {
        try
        {
            await GetRuntimeHost().RestoreBackgroundStateAsync();
        }
        catch
        {
            _foregroundController?.ExitForeground();
            _foregroundController?.RequestStop();
        }
    }

    private AndroidRuntimeServiceHost GetRuntimeHost() =>
        _runtimeHost ?? throw new InvalidOperationException("The Android runtime service is unavailable.");
}

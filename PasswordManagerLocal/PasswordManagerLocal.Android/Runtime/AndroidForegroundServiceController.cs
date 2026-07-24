using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using PasswordManagerLocal.Android.Runtime;

namespace PasswordManagerLocal.Android;

public sealed class AndroidForegroundServiceController : IAndroidForegroundServiceController
{
    public const string NotificationChannelId = "password_manager_background_sync";
    public const int NotificationId = 41010;

    private readonly PasswordManagerBackgroundService _service;
    private readonly object _gate = new();
    private bool _isForeground;
    private bool _isServiceStarted;

    public AndroidForegroundServiceController(PasswordManagerBackgroundService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public bool AreNotificationsEnabled
    {
        get
        {
            var manager = _service.GetSystemService(Context.NotificationService) as NotificationManager;
            return manager is not null && manager.AreNotificationsEnabled();
        }
    }

    public void EnsureServiceStarted()
    {
        lock (_gate)
        {
            if (_isServiceStarted)
                return;

            var intent = new Intent(_service, typeof(PasswordManagerBackgroundService));
            _service.StartForegroundService(intent);
            _isServiceStarted = true;
        }
    }

    public void MarkServiceStarted()
    {
        lock (_gate)
            _isServiceStarted = true;
    }

    public void EnterForeground(AndroidForegroundNotificationState state)
    {
        lock (_gate)
        {
            EnsureNotificationChannel();
            var notification = BuildNotification(state);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                _service.StartForeground(
                    NotificationId,
                    notification,
                    ForegroundService.TypeConnectedDevice);
            }
            else
            {
                _service.StartForeground(NotificationId, notification);
            }

            _isForeground = true;
        }
    }

    public void ExitForeground()
    {
        lock (_gate)
        {
            if (!_isForeground)
                return;

            _service.StopForeground(StopForegroundFlags.Remove);
            _isForeground = false;
        }
    }

    public void RequestStop()
    {
        lock (_gate)
        {
            if (!_isServiceStarted)
                return;

            _isServiceStarted = false;
            _service.StopSelf();
        }
    }

    private void EnsureNotificationChannel()
    {
        var manager = _service.GetSystemService(Context.NotificationService) as NotificationManager
            ?? throw new InvalidOperationException("The Android notification manager is unavailable.");
        var existing = manager.GetNotificationChannel(NotificationChannelId);
        if (existing is not null)
            return;

        var channel = new NotificationChannel(
            NotificationChannelId,
            "Background synchronization",
            NotificationImportance.Low)
        {
            Description = "Keeps already-enrolled password-manager devices synchronized."
        };
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(AndroidForegroundNotificationState state)
    {
        var launchIntent = new Intent(_service, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop | ActivityFlags.NewTask);
        var pendingIntent = PendingIntent.GetActivity(
            _service,
            0,
            launchIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new Notification.Builder(_service, NotificationChannelId)
            .SetSmallIcon(Resource.Drawable.icon)
            .SetContentTitle("Password Manager")
            .SetContentText(state == AndroidForegroundNotificationState.WaitingForDeviceUnlock
                ? "Background synchronization is waiting for device unlock"
                : "Background synchronization is active")
            .SetContentIntent(pendingIntent)
            .SetCategory(Notification.CategoryService)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .Build();
    }
}

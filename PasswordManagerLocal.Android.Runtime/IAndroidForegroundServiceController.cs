namespace PasswordManagerLocal.Android.Runtime;

public interface IAndroidForegroundServiceController
{
    bool AreNotificationsEnabled { get; }
    void EnsureServiceStarted();
    void EnterForeground(AndroidForegroundNotificationState state);
    void ExitForeground();
    void RequestStop();
}

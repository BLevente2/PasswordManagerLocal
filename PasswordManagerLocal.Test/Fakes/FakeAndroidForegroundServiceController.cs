using PasswordManagerLocal.Android.Runtime;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeAndroidForegroundServiceController : IAndroidForegroundServiceController
{
    public bool AreNotificationsEnabled { get; set; } = true;
    public int EnsureServiceStartedCalls { get; private set; }
    public int EnterForegroundCalls { get; private set; }
    public int ExitForegroundCalls { get; private set; }
    public int RequestStopCalls { get; private set; }
    public bool IsForeground { get; private set; }
    public bool IsServiceStarted { get; private set; }
    public AndroidForegroundNotificationState? LastNotificationState { get; private set; }
    public Exception? EnterForegroundFailure { get; set; }

    public void EnsureServiceStarted()
    {
        if (IsServiceStarted)
            return;

        EnsureServiceStartedCalls++;
        IsServiceStarted = true;
    }

    public void EnterForeground(AndroidForegroundNotificationState state)
    {
        EnterForegroundCalls++;
        LastNotificationState = state;
        if (EnterForegroundFailure is not null)
            throw EnterForegroundFailure;
        IsForeground = true;
    }

    public void ExitForeground()
    {
        ExitForegroundCalls++;
        IsForeground = false;
    }

    public void RequestStop()
    {
        RequestStopCalls++;
        IsServiceStarted = false;
    }
}

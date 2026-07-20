namespace PasswordManagerLocal.Runtime.Abstractions;

public enum BackendRuntimeState
{
    NotStarted,
    Starting,
    Ready,
    WaitingForDeviceUnlock,
    Failed,
    Stopping,
    Stopped
}

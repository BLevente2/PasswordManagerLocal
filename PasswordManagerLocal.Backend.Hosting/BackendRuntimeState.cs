namespace PasswordManagerLocal.Backend.Hosting;

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

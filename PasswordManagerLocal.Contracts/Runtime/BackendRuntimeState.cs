namespace PasswordManagerLocal.Contracts.Runtime;

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

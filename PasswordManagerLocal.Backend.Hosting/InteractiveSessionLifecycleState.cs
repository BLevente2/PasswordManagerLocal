namespace PasswordManagerLocal.Backend.Hosting;

public enum InteractiveSessionLifecycleState
{
    None,
    Opening,
    Active,
    Closing,
    CleanupFailed
}

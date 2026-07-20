namespace PasswordManagerLocal.Runtime.Abstractions;

[Flags]
public enum BackendLifetimeReason
{
    None = 0,
    InteractiveUi = 1,
    BackgroundSync = 2
}

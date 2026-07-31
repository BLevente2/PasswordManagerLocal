namespace PasswordManagerLocal.Contracts.Runtime;

public enum BackendRuntimeFailureKind
{
    None,
    DatabaseCompatibility,
    PlatformKeyUnavailable,
    StorageUnavailable,
    StartupFailure,
    InteractiveCleanupFailure,
    ShutdownFailure
}

namespace PasswordManagerLocal.Runtime.Abstractions;

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

namespace PasswordManagerLocal.Backend.Hosting;

public enum BackendRuntimeFailureKind
{
    None,
    DatabaseCompatibility,
    PlatformKeyUnavailable,
    StorageUnavailable,
    StartupFailure
}

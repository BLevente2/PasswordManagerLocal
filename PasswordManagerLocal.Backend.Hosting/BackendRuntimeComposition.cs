namespace PasswordManagerLocal.Backend.Hosting;

public sealed record BackendRuntimeComposition(
    IBackendRuntime Runtime,
    IBackendRuntimeLifetimeCoordinator LifetimeCoordinator,
    string ApplicationDataDirectory);

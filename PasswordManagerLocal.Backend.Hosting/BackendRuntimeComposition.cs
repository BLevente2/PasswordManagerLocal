namespace PasswordManagerLocal.Backend.Hosting;

public sealed record BackendRuntimeComposition(
    IBackendRuntime Runtime,
    string ApplicationDataDirectory);

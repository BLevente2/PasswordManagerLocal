using PasswordManagerLocal.Backend.Hosting;

namespace PasswordManagerLocal.Frontend;

public sealed record FrontendApplicationContext
{
    public FrontendApplicationContext(
        IBackendRuntime backendRuntime,
        string applicationDataDirectory)
    {
        BackendRuntime = backendRuntime ?? throw new ArgumentNullException(nameof(backendRuntime));
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        ApplicationDataDirectory = Path.GetFullPath(applicationDataDirectory);
    }

    public IBackendRuntime BackendRuntime { get; }
    public string ApplicationDataDirectory { get; }
}

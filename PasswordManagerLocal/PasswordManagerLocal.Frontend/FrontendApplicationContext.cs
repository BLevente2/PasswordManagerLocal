using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Frontend.Services;

namespace PasswordManagerLocal.Frontend;

public sealed record FrontendApplicationContext
{
    public FrontendApplicationContext(
        IFrontendBackendClient<IEndpoints> backendClient,
        IBackgroundSyncSettingsClient backgroundSyncSettingsClient,
        string applicationDataDirectory)
    {
        BackendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
        BackgroundSyncSettingsClient = backgroundSyncSettingsClient
            ?? throw new ArgumentNullException(nameof(backgroundSyncSettingsClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        ApplicationDataDirectory = Path.GetFullPath(applicationDataDirectory);
    }

    public IFrontendBackendClient<IEndpoints> BackendClient { get; }
    public IBackgroundSyncSettingsClient BackgroundSyncSettingsClient { get; }
    public string ApplicationDataDirectory { get; }
}

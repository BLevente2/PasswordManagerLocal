using PasswordManagerLocal.Contracts.BackgroundSync;
using PasswordManagerLocal.Contracts.Endpoints;
using PasswordManagerLocal.Contracts.Runtime;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Preferences;

namespace PasswordManagerLocal.Frontend;

public sealed record FrontendApplicationContext
{
    public FrontendApplicationContext(
        IFrontendBackendClient<IEndpoints> backendClient,
        IBackgroundSyncSettingsClient backgroundSyncSettingsClient,
        string applicationDataDirectory,
        Action? desktopExitRequested = null,
        IApplicationPreferencesStore? applicationPreferencesStore = null)
    {
        BackendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
        BackgroundSyncSettingsClient = backgroundSyncSettingsClient
            ?? throw new ArgumentNullException(nameof(backgroundSyncSettingsClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        ApplicationDataDirectory = Path.GetFullPath(applicationDataDirectory);
        DesktopExitRequested = desktopExitRequested;
        ApplicationPreferencesStore = applicationPreferencesStore ??
            new FileApplicationPreferencesStore(ApplicationDataDirectory);
    }

    public IFrontendBackendClient<IEndpoints> BackendClient { get; }
    public IBackgroundSyncSettingsClient BackgroundSyncSettingsClient { get; }
    public string ApplicationDataDirectory { get; }
    public Action? DesktopExitRequested { get; }
    public IApplicationPreferencesStore ApplicationPreferencesStore { get; }
}

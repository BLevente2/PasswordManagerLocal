using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Frontend;

public sealed record FrontendApplicationContext
{
    public FrontendApplicationContext(
        IFrontendBackendClient<IEndpoints> backendClient,
        IBackgroundSyncSettingsStore backgroundSyncSettingsStore,
        string applicationDataDirectory,
        bool isBackgroundSyncSettingAvailable = true)
    {
        BackendClient = backendClient ?? throw new ArgumentNullException(nameof(backendClient));
        BackgroundSyncSettingsStore = backgroundSyncSettingsStore
            ?? throw new ArgumentNullException(nameof(backgroundSyncSettingsStore));
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        ApplicationDataDirectory = Path.GetFullPath(applicationDataDirectory);
        IsBackgroundSyncSettingAvailable = isBackgroundSyncSettingAvailable;
    }

    public IFrontendBackendClient<IEndpoints> BackendClient { get; }
    public IBackgroundSyncSettingsStore BackgroundSyncSettingsStore { get; }
    public string ApplicationDataDirectory { get; }
    public bool IsBackgroundSyncSettingAvailable { get; }
}

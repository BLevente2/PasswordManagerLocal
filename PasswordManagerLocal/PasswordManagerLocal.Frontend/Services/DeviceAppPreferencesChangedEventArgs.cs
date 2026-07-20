namespace PasswordManagerLocal.Frontend.Services;

public sealed class DeviceAppPreferencesChangedEventArgs : EventArgs
{
    public DeviceAppPreferencesChangedEventArgs(bool backgroundSyncEnabled)
    {
        BackgroundSyncEnabled = backgroundSyncEnabled;
    }

    public bool BackgroundSyncEnabled { get; }
}

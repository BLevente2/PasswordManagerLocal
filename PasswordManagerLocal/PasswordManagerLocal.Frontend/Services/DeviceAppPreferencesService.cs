namespace PasswordManagerLocal.Frontend.Services;

public sealed class DeviceAppPreferencesService
{
    private bool _backgroundSyncEnabled;

    public DeviceAppPreferencesService()
    {
        _backgroundSyncEnabled = AppConfigurationManager.GetBackgroundSyncEnabled();
    }

    public event EventHandler<DeviceAppPreferencesChangedEventArgs>? PreferencesChanged;

    public bool BackgroundSyncEnabled
    {
        get => _backgroundSyncEnabled;
        set
        {
            if (_backgroundSyncEnabled == value)
                return;

            _backgroundSyncEnabled = value;
            AppConfigurationManager.SaveBackgroundSyncEnabled(value);
            PreferencesChanged?.Invoke(
                this,
                new DeviceAppPreferencesChangedEventArgs(value));
        }
    }
}

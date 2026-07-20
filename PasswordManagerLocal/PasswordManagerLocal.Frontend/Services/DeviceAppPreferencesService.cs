using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Frontend.Services;

public sealed class DeviceAppPreferencesService
{
    private readonly IBackgroundSyncSettingsStore _backgroundSyncSettingsStore;
    private bool _backgroundSyncEnabled;

    public DeviceAppPreferencesService(IBackgroundSyncSettingsStore backgroundSyncSettingsStore)
    {
        _backgroundSyncSettingsStore = backgroundSyncSettingsStore
            ?? throw new ArgumentNullException(nameof(backgroundSyncSettingsStore));
        _backgroundSyncEnabled = _backgroundSyncSettingsStore
            .ReadAsync()
            .GetAwaiter()
            .GetResult()
            .IsEnabled;
    }

    public event EventHandler<DeviceAppPreferencesChangedEventArgs>? PreferencesChanged;

    public bool BackgroundSyncEnabled
    {
        get => _backgroundSyncEnabled;
        set
        {
            if (_backgroundSyncEnabled == value)
                return;

            _backgroundSyncSettingsStore
                .WriteAsync(new BackgroundSyncSettings(value))
                .GetAwaiter()
                .GetResult();
            _backgroundSyncEnabled = value;
            PreferencesChanged?.Invoke(
                this,
                new DeviceAppPreferencesChangedEventArgs(value));
        }
    }
}

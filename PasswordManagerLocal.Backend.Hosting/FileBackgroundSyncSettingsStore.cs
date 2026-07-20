using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Runtime.Abstractions;
using System.Text;
using System.Text.Json;

namespace PasswordManagerLocal.Backend.Hosting;

public sealed class FileBackgroundSyncSettingsStore : IBackgroundSyncSettingsStore
{
    private readonly string _settingsPath;
    private readonly object _gate = new();

    public FileBackgroundSyncSettingsStore(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        _settingsPath = Path.Combine(
            Path.GetFullPath(applicationDataDirectory),
            ApplicationFileNames.BackgroundSyncSettingsFileName);
    }

    public Task<BackgroundSyncSettings> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(_settingsPath))
                return Task.FromResult(new BackgroundSyncSettings(false));

            var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize(
                json,
                BackgroundSyncSettingsJsonContext.Default.BackgroundSyncSettings);

            return Task.FromResult(settings ?? throw new InvalidDataException(
                "The background synchronization settings file is empty."));
        }
    }

    public Task WriteAsync(
        BackgroundSyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                var json = JsonSerializer.Serialize(
                    settings,
                    BackgroundSyncSettingsJsonContext.Default.BackgroundSyncSettings);
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                File.Move(temporaryPath, _settingsPath, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }

        return Task.CompletedTask;
    }
}

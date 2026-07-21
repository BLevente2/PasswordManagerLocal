using System.Text.Json;

namespace PasswordManagerLocal.Windows.Agent.Status;

public sealed class WindowsBackgroundSyncSettingsReader : IWindowsBackgroundSyncSettingsReader
{
    private readonly string _settingsPath;

    public WindowsBackgroundSyncSettingsReader(string applicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        _settingsPath = Path.Combine(
            Path.GetFullPath(applicationDataDirectory),
            "background-sync.json");
    }

    public async Task<bool> ReadIsEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
            return false;

        await using var stream = new FileStream(
            _settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("isEnabled", out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            value.GetBoolean();
    }
}

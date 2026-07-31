using PasswordManagerLocal.Contracts.Preferences;

namespace PasswordManagerLocal.Preferences;

public interface IApplicationPreferencesStore
{
    Task<ApplicationPreferences> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(
        ApplicationPreferences preferences,
        CancellationToken cancellationToken = default);
}

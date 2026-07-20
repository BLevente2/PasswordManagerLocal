using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IRememberMeService
{
    Task<IReadOnlyList<Guid>> RestoreRememberedSessionsAsync(CancellationToken ct = default);
    Task<Guid> InitializeRememberMeSessionAsync(Guid userId, CancellationToken ct = default);
    Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default);
    void SetRememberMe(User user, bool rememberMe, EncryptionKey key);
}

using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IRememberMeService
{
    Task<IReadOnlyList<string>> InicializeAllRememberMeAsync(CancellationToken ct = default);
    Task SetRememberMeAsync(string token, bool rememberMe, CancellationToken ct = default);
    void SetRememberMe(User user, bool rememberMe, EncryptionKey key);
}
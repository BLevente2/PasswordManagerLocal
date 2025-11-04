using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IKeyVaultService
{
    void SetUserKey(string token, EncryptionKey key, DateTimeOffset? expiresAt = null);
    bool RotateUserKey(string token, EncryptionKey newKey, DateTimeOffset? newExpiresAt = null);
    bool HasUserKey(string token);
    bool TryGetEncryptionKey(string token, out EncryptionKey key);
    void InvalidateToken(string token);
    int PurgeExpired();
}
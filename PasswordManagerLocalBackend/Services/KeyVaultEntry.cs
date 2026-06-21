using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Services;

internal sealed class KeyVaultEntry
{
    public EncryptionKey Key;
    public DateTimeOffset ExpiresAt;

    public KeyVaultEntry(EncryptionKey key, DateTimeOffset exp)
    {
        Key = key;
        ExpiresAt = exp;
    }
}

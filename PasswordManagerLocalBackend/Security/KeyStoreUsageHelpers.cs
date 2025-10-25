namespace PasswordManagerLocalBackend.Security;

public static class KeyStoreUsageHelpers
{
    public static async Task StoreEncryptionKeyAsync(IKeyStore store, string id, EncryptionKey key, IKeyProtector protector)
    {
        var raw = key.ExportCopy();
        try
        {
            var wrapped = protector.Protect(raw);
            await store.SaveAsync(id, wrapped);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw);
        }
    }

    public static async Task<EncryptionKey> LoadEncryptionKeyAsync(IKeyStore store, string id, IKeyProtector protector)
    {
        var wrapped = await store.LoadAsync(id);
        var raw = protector.Unprotect(wrapped);
        return EncryptionKey.FromRaw(raw);
    }
}
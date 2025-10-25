namespace PasswordManagerLocalBackend.Security;

public interface IKeyStore
{
    Task SaveAsync(string id, ReadOnlySpan<byte> protectedKey);
    Task<byte[]> LoadAsync(string id);
    Task<bool> ExistsAsync(string id);
}
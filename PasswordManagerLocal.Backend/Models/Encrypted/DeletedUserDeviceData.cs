using PasswordManagerLocal.Backend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocal.Backend.Models.Encrypted;

public sealed class DeletedUserDeviceData : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public Guid Id { get; set; }
    public DateTimeOffset DeletedAt { get; set; } = DateTimeOffset.UtcNow;

    public void Dispose()
    {
        if (_disposed)
            return;

        Id = Guid.Empty;
        DeletedAt = default;
        CryptographicOperations.ZeroMemory(IntegrityHash);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public override byte[] CalculateIntegrityHash() =>
        Hashing.SHA256Hash(hash =>
        {
            hash.Write(Id);
            hash.Write(DeletedAt);
        });
}

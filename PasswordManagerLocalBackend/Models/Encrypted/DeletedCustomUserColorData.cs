using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;
using PasswordManagerLocalBackend.Utils;

namespace PasswordManagerLocalBackend.Models.Encrypted;

public sealed class DeletedCustomUserColorData : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public Guid Id { get; set; }
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;

    public void Dispose()
    {
        if (_disposed)
            return;

        Id = Guid.Empty;
        DeletedAt = UtcDateTimeUtil.MinDateTime;
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

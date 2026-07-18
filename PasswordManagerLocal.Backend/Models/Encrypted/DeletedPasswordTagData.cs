using PasswordManagerLocal.Backend.Security;
using System.Security.Cryptography;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Models.Encrypted;

public sealed class DeletedPasswordTagData : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public Guid Id { get; set; }
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
    public SyncVersionStamp Version { get; set; } = new();

    public void Dispose()
    {
        if (_disposed)
            return;

        Id = Guid.Empty;
        DeletedAt = UtcDateTimeUtil.MinDateTime;
        Version = new();
        CryptographicOperations.ZeroMemory(IntegrityHash);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public override byte[] CalculateIntegrityHash() =>
        Hashing.SHA256Hash(hash =>
        {
            hash.Write(Id);
            hash.Write(DeletedAt);
            Version.WriteTo(hash);
        });
}

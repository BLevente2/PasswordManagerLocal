using PasswordManagerLocalBackend.Security;
using static PasswordManagerLocalBackend.Constants.PasswordConstants;
using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Models.Encrypted;

public sealed class PasswordTag : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = DefaultPasswordColor;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    public void Dispose()
    {
        if (_disposed)
            return;

        Id = Guid.Empty;
        Name = string.Empty;
        Color = string.Empty;
        LastUpdatedAt = DateTime.MinValue;
        CryptographicOperations.ZeroMemory(IntegrityHash);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public override byte[] CalculateIntegrityHash() =>
        Hashing.SHA256Hash(hash =>
        {
            hash.Write(Id);
            hash.WriteString(Name);
            hash.WriteString(Color);
            hash.Write(LastUpdatedAt);
        });
}

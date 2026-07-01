using PasswordManagerLocalBackend.Security;
using static PasswordManagerLocalBackend.Constants.PasswordConstants;
using System.Security.Cryptography;
using PasswordManagerLocalBackend.Utils;

namespace PasswordManagerLocalBackend.Models.Encrypted;

public sealed class SecurePassword : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = DefaultPasswordColor;
    public byte[] Password { get; set; } = [];
    public List<Guid> TagIds { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;


    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        Id = Guid.Empty;
        Name = string.Empty;
        Description = string.Empty;
        Color = string.Empty;
        CreatedAt = UtcDateTimeUtil.MinDateTime;
        LastUpdatedAt = UtcDateTimeUtil.MinDateTime;
        CryptographicOperations.ZeroMemory(Password);
        TagIds.Clear();
        CryptographicOperations.ZeroMemory(IntegrityHash);

        _disposed = true;
    }



    public override byte[] CalculateIntegrityHash() =>
        Hashing.SHA256Hash(hash =>
        {
            hash.Write(Id);
            hash.WriteString(Name);
            hash.WriteString(Description);
            hash.WriteString(Color);
            hash.WriteBytes(Password);
            hash.Write(TagIds.Count);
            foreach (var tagId in TagIds.Order())
                hash.Write(tagId);
            hash.Write(CreatedAt);
            hash.Write(LastUpdatedAt);
        });

}

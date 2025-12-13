using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Models.Encrypted;

public sealed class SecurePassword : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#FFFFD700"; // Classic gold default color
    public byte[] Password { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    ~SecurePassword()
    {
        Dispose(disposing: true);
    }


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
        CreatedAt = DateTime.MinValue;
        LastUpdatedAt = DateTime.MinValue;
        CryptographicOperations.ZeroMemory(Password);
        CryptographicOperations.ZeroMemory(IntegrityHash);

        _disposed = true;
    }



    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Id.ToByteArray());
        bw.Write(Name);
        bw.Write(Description);
        bw.Write(Color);
        bw.Write(Password);
        bw.Write(CreatedAt.ToBinary());
        bw.Write(LastUpdatedAt.ToBinary());

        return Hashing.SHA512Hash(ms.ToArray());
    }
}
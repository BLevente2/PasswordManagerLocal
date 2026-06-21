using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Models.Encrypted;

public sealed class UserDeviceData : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;

    public void Dispose()
    {
        if (_disposed)
            return;

        Id = Guid.Empty;
        Name = string.Empty;
        LinkedAt = default;
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(IntegrityHash);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Id.ToByteArray());
        bw.Write(Name);
        bw.Write(LinkedAt.ToUnixTimeMilliseconds());

        return Hashing.SHA256Hash(ms.ToArray());
    }
}

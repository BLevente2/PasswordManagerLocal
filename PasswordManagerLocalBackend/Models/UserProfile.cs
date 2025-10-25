using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Models;

public sealed class UserProfile : IDisposable
{
    private bool _disposed;

    public Guid UId { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginDate { get; set; } = DateTime.UtcNow;

    ~UserProfile()
    {
        Dispose(disposing: true);
    }




    public byte[] IntegrityHash { get; set; } = [];

    public byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(UId.ToByteArray());
        bw.Write(Username);
        bw.Write(FirstName);
        bw.Write(LastName);
        bw.Write(Email);
        bw.Write(RegistrationDate.ToBinary());
        bw.Write(LastLoginDate.ToBinary());

        return Hashing.SHA512Hash(ms.ToArray());
    }

    public bool IsIntegrityValid() => Hashing.Verify(IntegrityHash, CalculateIntegrityHash());

    public void GenerateIntegrityHash() => IntegrityHash = CalculateIntegrityHash();





    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            UId = Guid.Empty;
            Username = string.Empty;
            FirstName = string.Empty;
            LastName = string.Empty;
            Email = string.Empty;
            RegistrationDate = DateTime.MinValue;
            LastLoginDate = DateTime.MinValue;
        }

        _disposed = true;
    }
}
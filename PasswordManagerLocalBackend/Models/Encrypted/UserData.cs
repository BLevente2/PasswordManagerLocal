using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Models.Encrypted;

public sealed class UserData : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public Guid UId { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginDate { get; set; } = DateTime.UtcNow;

    public List<SecurePassword> Passwords { get; set; } = [];

    ~UserData()
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

        UId = Guid.Empty;
        Username = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        Email = string.Empty;
        RegistrationDate = DateTime.MinValue;
        LastLoginDate = DateTime.MinValue;
        CryptographicOperations.ZeroMemory(IntegrityHash);

        if (disposing)
            Passwords.ForEach(pw => pw.Dispose());
        Passwords.Clear();

        _disposed = true;
    }



    public override byte[] CalculateIntegrityHash()
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
        bw.Write(Passwords.Count);

        Passwords.ForEach(pw => bw.Write(pw.IntegrityHash));

        return Hashing.SHA512Hash(ms.ToArray());
    }
}
using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Models.Encrypted;

public sealed class SecurePasswords : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public List<SecurePassword> Passwords { get; set; } = [];
    public byte[] PasswordKey { get; set; } = [];



    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }


    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        CryptographicOperations.ZeroMemory(PasswordKey);
        if (disposing)
            Passwords.ForEach(pw => pw.Dispose());
        Passwords.Clear();

        _disposed = true;
    }


    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(PasswordKey);
        bw.Write(Passwords.Count);

        Passwords.ForEach(pw => bw.Write(pw.IntegrityHash));

        return Hashing.SHA512Hash(ms.ToArray());
    }
}
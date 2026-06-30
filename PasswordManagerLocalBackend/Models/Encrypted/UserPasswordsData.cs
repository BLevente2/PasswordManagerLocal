using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Models.Encrypted;

public sealed class UserPasswordsData : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public List<SecurePassword> Passwords { get; set; } = [];
    public List<DeletedPasswordData> DeletedPasswords { get; set; } = [];
    public List<CustomUserColor> CustomColors { get; set; } = [];
    public List<DeletedCustomUserColorData> DeletedCustomColors { get; set; } = [];
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
        CryptographicOperations.ZeroMemory(IntegrityHash);
        if (disposing)
        {
            Passwords.ForEach(pw => pw.Dispose());
            DeletedPasswords.ForEach(deleted => deleted.Dispose());
            CustomColors.ForEach(color => color.Dispose());
            DeletedCustomColors.ForEach(deleted => deleted.Dispose());
        }
        Passwords.Clear();
        DeletedPasswords.Clear();
        CustomColors.Clear();
        DeletedCustomColors.Clear();

        _disposed = true;
    }

    public override byte[] CalculateIntegrityHash() =>
        Hashing.SHA256Hash(hash =>
        {
            hash.WriteBytes(PasswordKey);
            hash.Write(Passwords.Count);
            foreach (var password in Passwords.OrderBy(password => password.Id))
                hash.WriteBytes(password.IntegrityHash);
            hash.Write(DeletedPasswords.Count);
            foreach (var deleted in DeletedPasswords.OrderBy(deleted => deleted.Id))
                hash.WriteBytes(deleted.IntegrityHash);
            hash.Write(CustomColors.Count);
            foreach (var color in CustomColors.OrderBy(color => color.Id))
                hash.WriteBytes(color.IntegrityHash);
            hash.Write(DeletedCustomColors.Count);
            foreach (var deleted in DeletedCustomColors.OrderBy(deleted => deleted.Id))
                hash.WriteBytes(deleted.IntegrityHash);
        });

}

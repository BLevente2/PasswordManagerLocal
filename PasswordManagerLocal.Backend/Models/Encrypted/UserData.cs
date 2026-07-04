using PasswordManagerLocal.Backend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocal.Backend.Models.Encrypted;

public sealed class UserData : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public Guid UId { get; set; } = Guid.NewGuid();
    public byte[] GeneralUserDataKey { get; set; } = [];
    public byte[] GeneralUserDataIntegrityHash { get; set; } = [];
    public byte[] UserPasswordsDataKey { get; set; } = [];
    public byte[] UserPasswordsDataIntegrityHash { get; set; } = [];
    public byte[] UserDevicesDataKey { get; set; } = [];
    public byte[] UserDevicesDataIntegrityHash { get; set; } = [];

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
        CryptographicOperations.ZeroMemory(GeneralUserDataKey);
        CryptographicOperations.ZeroMemory(GeneralUserDataIntegrityHash);
        CryptographicOperations.ZeroMemory(UserPasswordsDataKey);
        CryptographicOperations.ZeroMemory(UserPasswordsDataIntegrityHash);
        CryptographicOperations.ZeroMemory(UserDevicesDataKey);
        CryptographicOperations.ZeroMemory(UserDevicesDataIntegrityHash);
        CryptographicOperations.ZeroMemory(IntegrityHash);

        _disposed = true;
    }

    public override byte[] CalculateIntegrityHash() =>
        Hashing.SHA256Hash(hash =>
        {
            hash.Write(UId);
            hash.WriteBytes(GeneralUserDataKey);
            hash.WriteBytes(GeneralUserDataIntegrityHash);
            hash.WriteBytes(UserPasswordsDataKey);
            hash.WriteBytes(UserPasswordsDataIntegrityHash);
            hash.WriteBytes(UserDevicesDataKey);
            hash.WriteBytes(UserDevicesDataIntegrityHash);
        });

}

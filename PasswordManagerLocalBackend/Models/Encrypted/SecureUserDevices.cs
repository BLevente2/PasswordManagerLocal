using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Models.Encrypted;

public sealed class SecureUserDevices : IntegrityCheckableBase, IDisposable
{
    private bool _disposed;

    public List<UserDeviceData> Devices { get; set; } = [];

    public void Dispose()
    {
        if (_disposed)
            return;

        Devices.ForEach(device => device.Dispose());
        Devices.Clear();
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(IntegrityHash);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Devices.Count);
        foreach (var device in Devices)
            bw.Write(device.IntegrityHash);

        return Hashing.SHA512Hash(ms.ToArray());
    }
}

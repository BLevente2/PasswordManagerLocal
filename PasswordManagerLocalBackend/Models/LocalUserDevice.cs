using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Models;

public sealed class LocalUserDevice : IntegrityCheckableBase
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid LocalDeviceIdentityId { get; set; }
    public LocalDeviceIdentity? LocalDeviceIdentity { get; set; }

    public bool IsSyncOn { get; set; } = true;

    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(UserId.ToByteArray());
        bw.Write(LocalDeviceIdentityId.ToByteArray());
        bw.Write(IsSyncOn);

        return Hashing.SHA256Hash(ms.ToArray());
    }
}

using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Models;

public sealed class UserDevice : IntegrityCheckableBase
{
    public Guid ModelId { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    public bool IsSyncOn { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.UtcNow;


    public override void GenerateIntegrityHash()
    {
        ModelId = Sync.SyncIdentityUtil.BuildUserDeviceModelId(UserId, DeviceId);
        base.GenerateIntegrityHash();
    }

    public override bool IsIntegrityValid() =>
        ModelId == Sync.SyncIdentityUtil.BuildUserDeviceModelId(UserId, DeviceId) && base.IsIntegrityValid();

    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(ModelId.ToByteArray());
        bw.Write(UserId.ToByteArray());
        bw.Write(DeviceId.ToByteArray());
        bw.Write(IsSyncOn);
        bw.Write(IsDeleted);
        bw.Write(DeletedAt?.ToUnixTimeMilliseconds() ?? 0);
        bw.Write(LastModifiedAt.ToUnixTimeMilliseconds());

        return Hashing.SHA512Hash(ms.ToArray());
    }
}

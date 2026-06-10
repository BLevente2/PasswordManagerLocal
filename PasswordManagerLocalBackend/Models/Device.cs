using PasswordManagerLocalBackend.Security;
using System.Text;

namespace PasswordManagerLocalBackend.Models;

public sealed class Device : IntegrityCheckableBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public byte[] PublicKey { get; set; } = [];
    public byte[] SignPublicKey { get; set; } = [];
    public string TlsCertFingerprint { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public byte[] LastKnownHash { get; set; } = [];

    public DateTime LastSync { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public bool IsTrusted { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }
    public int InvalidSyncAttemptCount { get; set; }
    public DateTimeOffset? LastInvalidSyncAttemptAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<User> Users { get; set; } = [];
    public ICollection<UserDevice> UserDevices { get; set; } = [];
    public ICollection<SyncQueueItem> ItemsNeedingSync { get; set; } = [];

    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Id.ToByteArray());
        bw.Write(PublicKey);
        bw.Write(SignPublicKey);
        bw.Write(Encoding.UTF8.GetBytes(TlsCertFingerprint));
        bw.Write(Encoding.UTF8.GetBytes(DeviceName ?? string.Empty));
        bw.Write(LastKnownHash);
        bw.Write(LastSync.ToBinary());
        bw.Write(LastSeen.ToBinary());
        bw.Write(IsTrusted ? (byte)1 : (byte)0);
        bw.Write(IsBlocked ? (byte)1 : (byte)0);
        bw.Write(Encoding.UTF8.GetBytes(BlockedReason ?? string.Empty));
        bw.Write(BlockedAt?.ToUnixTimeMilliseconds() ?? 0);
        bw.Write(InvalidSyncAttemptCount);
        bw.Write(LastInvalidSyncAttemptAt?.ToUnixTimeMilliseconds() ?? 0);
        bw.Write(LastModifiedAt.ToUnixTimeMilliseconds());

        foreach (var userId in UserDevices.Where(ud => !ud.IsDeleted).Select(ud => ud.UserId).OrderBy(id => id))
            bw.Write(userId.ToByteArray());

        return Hashing.SHA512Hash(ms.ToArray());
    }
}

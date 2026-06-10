using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Sync;

public sealed class SyncDeltaPayload
{
    public Guid ModelId { get; set; }
    public SyncModelType ModelType { get; set; }
    public SyncChangeType ChangeType { get; set; }
    public UserSyncPayload? User { get; set; }
    public GroupSyncPayload? Group { get; set; }
    public DeviceSyncPayload? Device { get; set; }
    public UserDeviceSyncPayload? UserDevice { get; set; }
}

public sealed class UserSyncPayload
{
    public Guid UId { get; set; }
    public byte[] UsernameHash { get; set; } = [];
    public byte[] UsernameSalt { get; set; } = [];
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] EncryptedPayload { get; set; } = [];
    public byte[] IntegrityHash { get; set; } = [];
    public List<Guid> GroupIds { get; set; } = [];
    public List<Guid> DeviceIds { get; set; } = [];
}

public sealed class GroupSyncPayload
{
    public Guid Id { get; set; }
    public byte[] EncryptedPayload { get; set; } = [];
    public byte[] IntegrityHash { get; set; } = [];
    public List<Guid> UserIds { get; set; } = [];
}

public sealed class DeviceSyncPayload
{
    public Guid Id { get; set; }
    public byte[] PublicKey { get; set; } = [];
    public byte[] SignPublicKey { get; set; } = [];
    public string TlsCertFingerprint { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public byte[] LastKnownHash { get; set; } = [];
    public DateTime LastSync { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsTrusted { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }
    public int InvalidSyncAttemptCount { get; set; }
    public DateTimeOffset? LastInvalidSyncAttemptAt { get; set; }
    public byte[] IntegrityHash { get; set; } = [];
    public List<Guid> UserIds { get; set; } = [];
}

public sealed class UserDeviceSyncPayload
{
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSyncEnabled { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public byte[] IntegrityHash { get; set; } = [];
}

using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Sync;

public sealed class DeviceEnrollmentUserDeviceSnapshot
{
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public bool IsSyncOn { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; }
    public byte[] IntegrityHash { get; set; } = [];
}

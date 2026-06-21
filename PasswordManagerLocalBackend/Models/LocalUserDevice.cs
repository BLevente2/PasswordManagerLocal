namespace PasswordManagerLocalBackend.Models;

public sealed class LocalUserDevice
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid LocalDeviceIdentityId { get; set; }
    public LocalDeviceIdentity? LocalDeviceIdentity { get; set; }

    public bool IsSyncOn { get; set; } = true;
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}

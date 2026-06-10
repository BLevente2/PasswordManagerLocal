namespace PasswordManagerLocalBackend.Models;

public sealed class UserDevice
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    public string Name { get; set; } = string.Empty;
    public bool IsSyncEnabled { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

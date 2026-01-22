namespace PasswordManagerLocalBackend.Models;

public sealed class SyncItem
{
    public long QueueId { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ModelId { get; set; } = Guid.Empty;
    public SyncModelType ModelType { get; set; } = SyncModelType.None;
    public SyncChangeType ChangeType { get; set; } = SyncChangeType.None;

    public Guid DeviceId { get; set; } = Guid.Empty;
    public required Device DeviceNeedingSync { get; set; }

    public DateTime EnquedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; } = null;
}
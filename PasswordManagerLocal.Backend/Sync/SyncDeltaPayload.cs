using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Sync;

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


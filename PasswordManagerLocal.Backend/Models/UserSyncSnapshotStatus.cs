namespace PasswordManagerLocal.Backend.Models;

public enum UserSyncSnapshotStatus : byte
{
    Pending = 0,
    LocalPublished = 1,
    Quarantined = 2,
    MergedReceipt = 3
}

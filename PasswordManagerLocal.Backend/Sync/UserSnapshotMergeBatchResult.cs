namespace PasswordManagerLocal.Backend.Sync;

public sealed record UserSnapshotMergeEntryResult(
    Guid OriginDeviceId,
    Guid OriginInstanceId,
    long OriginRevision,
    bool Verified,
    string? FailureReason = null);

public sealed record UserSnapshotMergeBatchResult(
    bool CanonicalChanged,
    IReadOnlyList<UserSnapshotMergeEntryResult> Entries);

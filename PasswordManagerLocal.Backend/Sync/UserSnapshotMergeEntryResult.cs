using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Sync;

public sealed record UserSnapshotMergeEntryResult(
    Guid OriginDeviceId,
    Guid OriginInstanceId,
    long OriginRevision,
    bool Verified,
    string? FailureReason = null,
    UserDataVerificationState VerificationState = UserDataVerificationState.Healthy,
    UserDataBlobKind FailedBlobs = UserDataBlobKind.None,
    string? DiagnosticCode = null);

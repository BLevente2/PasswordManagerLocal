using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Sync;


public sealed record UserSnapshotMergeBatchResult(
    bool CanonicalChanged,
    IReadOnlyList<UserSnapshotMergeEntryResult> Entries,
    UserDataVerificationState CanonicalState = UserDataVerificationState.Healthy,
    UserDataBlobKind CanonicalFailedBlobs = UserDataBlobKind.None,
    string? CanonicalDiagnosticCode = null)
{
    public bool CanonicalVerified => CanonicalState == UserDataVerificationState.Healthy;
}

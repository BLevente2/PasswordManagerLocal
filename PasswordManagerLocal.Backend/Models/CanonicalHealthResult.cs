using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Models;

public sealed record CanonicalHealthResult(
    UserDataVerificationState State,
    UserDataBlobKind FailedBlobs,
    UserSyncKeyConfidence KeyConfidence,
    string DiagnosticCode)
{
    public bool RowIntegrityVerified { get; init; }
    public bool CheckpointVerified { get; init; }
    public bool FullyVerified { get; init; }
    public bool IsPublishable => RowIntegrityVerified && CheckpointVerified &&
                                 State is UserDataVerificationState.Healthy or UserDataVerificationState.KeyNotConfirmed;
}

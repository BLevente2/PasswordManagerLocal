using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Sync;

public sealed record RecoveryCandidateVerificationResult(
    Guid OriginDeviceId,
    Guid OriginInstanceId,
    long OriginRevision,
    byte[] SnapshotHash,
    RecoveryCandidateState State,
    UserDataBlobKind FailedComponents = UserDataBlobKind.None,
    string? DiagnosticCode = null)
{
    public bool IsHealthy => State == RecoveryCandidateState.Healthy;
}

public sealed record UserDataRecoveryReconstructionResult(
    bool Reconstructed,
    IReadOnlyList<RecoveryCandidateVerificationResult> Candidates,
    string? DiagnosticCode = null)
{
    public int HealthyCandidateCount => Candidates.Count(candidate => candidate.IsHealthy);
    public UserDataBlobKind RecoveredComponents { get; init; } = UserDataBlobKind.None;
    public bool UsedVerifiedCache { get; init; }
}

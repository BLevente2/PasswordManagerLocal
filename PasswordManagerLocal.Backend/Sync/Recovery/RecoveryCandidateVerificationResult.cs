using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Sync.Recovery;

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

using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Sync.Enrollment;

public sealed class DeviceEnrollmentPendingSnapshot
{
    public byte[] EnvelopePayload { get; set; } = [];
    public UserSyncSnapshotStatus Status { get; set; }
    public string? QuarantineReason { get; set; }
    public byte[]? ConflictingSnapshotHash { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}

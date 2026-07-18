using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Sync;

public sealed class DeviceEnrollmentControlOperationSnapshot
{
    public byte[] EnvelopePayload { get; set; } = [];
    public UserControlOperationStatus Status { get; set; }
    public string? StatusReason { get; set; }
    public byte[]? ConflictingOperationHash { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset? AppliedAtUtc { get; set; }
}

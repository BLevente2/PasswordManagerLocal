using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Sync;

public sealed class DeviceEnrollmentGroupSnapshot
{
    public Guid Id { get; set; }
    public byte[] EncryptedPayload { get; set; } = [];
    public DateTimeOffset LastModifiedAt { get; set; }
    public byte[] IntegrityHash { get; set; } = [];
    public List<Guid> UserIds { get; set; } = [];
}

using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Sync;

public sealed class DeviceEnrollmentUserSnapshot
{
    public Guid UId { get; set; }
    public byte[] UsernameHash { get; set; } = [];
    public byte[] UsernameSalt { get; set; } = [];
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] EncryptedPayload { get; set; } = [];
    public DateTimeOffset LastModifiedAt { get; set; }
    public byte[] IntegrityHash { get; set; } = [];
    public List<Guid> GroupIds { get; set; } = [];
}

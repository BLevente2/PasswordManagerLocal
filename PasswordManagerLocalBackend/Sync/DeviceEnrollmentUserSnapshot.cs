using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Sync;

public sealed class DeviceEnrollmentUserSnapshot
{
    public Guid UId { get; set; }
    public byte[] UsernameHash { get; set; } = [];
    public byte[] UsernameSalt { get; set; } = [];
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] EncryptedPayload { get; set; } = [];
    public byte[] EncryptedGeneralUserDataPayload { get; set; } = [];
    public byte[] EncryptedUserPasswordsDataPayload { get; set; } = [];
    public byte[] EncryptedUserDevicesDataPayload { get; set; } = [];
    public DateTimeOffset LastModifiedAt { get; set; }
    public DateTimeOffset UserDataLastModifiedAt { get; set; }
    public DateTimeOffset GeneralUserDataLastModifiedAt { get; set; }
    public DateTimeOffset UserPasswordsDataLastModifiedAt { get; set; }
    public DateTimeOffset UserDevicesDataLastModifiedAt { get; set; }
    public byte[] IntegrityHash { get; set; } = [];
    public List<Guid> GroupIds { get; set; } = [];
}

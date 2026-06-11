using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Models;

public sealed class User : IntegrityCheckableBase
{
    public Guid UId { get; set; } = Guid.NewGuid();
    public byte[] UsernameHash { get; set; } = [];
    public byte[] UsernameSalt { get; set; } = [];
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] EncryptedPayload { get; set; } = [];
    public byte[]? SavedKey { get; set; } = null;
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.UtcNow;


    public ICollection<Group> Groups { get; set; } = [];
    public ICollection<Device> Devices { get; set; } = [];
    public ICollection<UserDevice> UserDevices { get; set; } = [];





    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(UId.ToByteArray());
        bw.Write(UsernameHash);
        bw.Write(UsernameSalt);
        bw.Write(PasswordSalt);
        bw.Write(EncryptedPayload);
        bw.Write(LastModifiedAt.ToUnixTimeMilliseconds());

        return Hashing.SHA512Hash(ms.ToArray());
    }
}

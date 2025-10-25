using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Models;

public class User
{
    public Guid UId { get; set; } = Guid.NewGuid();
    public byte[] UsernameHash { get; set; } = [];
    public byte[] UsernameSalt { get; set; } = [];
    public byte[] PasswordHash { get; set; } = [];
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] EncryptedPayload { get; set; } = [];
    public byte[] EncryptedPayloadSalt { get; set; } = [];
    public byte[] EncryptedPayloadIV { get; set; } = [];


    public ICollection<Group> Groups { get; set; } = [];
    public ICollection<Device> Devices { get; set; } = [];




    public byte[] IntegrityHash { get; set; } = [];

    public byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(UId.ToByteArray());
        bw.Write(UsernameHash);
        bw.Write(UsernameSalt);
        bw.Write(PasswordHash);
        bw.Write(PasswordSalt);
        bw.Write(EncryptedPayload);
        bw.Write(EncryptedPayloadSalt);
        bw.Write(EncryptedPayloadIV);

        bw.Write(Groups.Count);
        bw.Write(Devices.Count);

        return Hashing.SHA512Hash(ms.ToArray());
    }

    public bool IsIntegrityValid() => Hashing.Verify(IntegrityHash, CalculateIntegrityHash());

    public void GenerateIntegrityHash() => IntegrityHash = CalculateIntegrityHash();
}
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Models;

public class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public byte[] EncryptedPayload { get; set; } = [];
    public byte[] EncryptedIV { get; set; } = [];


    public ICollection<User> Users { get; set; } = [];




    public byte[] IntegrityHash { get; set; } = [];

    public byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Id.ToByteArray());
        bw.Write(EncryptedPayload);
        bw.Write(EncryptedIV);
        bw.Write(Users.Count);

        return Hashing.SHA512Hash(ms.ToArray());
    }

    public bool IsIntegrityValid() => Hashing.Verify(IntegrityHash, CalculateIntegrityHash());

    public void GenerateIntegrityHash() => IntegrityHash = CalculateIntegrityHash();
}
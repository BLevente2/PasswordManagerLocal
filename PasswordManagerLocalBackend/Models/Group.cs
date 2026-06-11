using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Models;

public sealed class Group : IntegrityCheckableBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public byte[] EncryptedPayload { get; set; } = [];
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.UtcNow;


    public ICollection<User> Users { get; set; } = [];





    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Id.ToByteArray());
        bw.Write(EncryptedPayload);
        bw.Write(LastModifiedAt.ToUnixTimeMilliseconds());

        return Hashing.SHA512Hash(ms.ToArray());
    }
}

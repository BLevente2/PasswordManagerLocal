using PasswordManagerLocalBackend.Security;
using System.Text;

namespace PasswordManagerLocalBackend.Models;

public class Device
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public byte[] PublicKey { get; set; } = [];
    public byte[] SignPublicKey { get; set; } = [];
    public string TlsCertFingerprint { get; set; } = string.Empty;
    public byte[] LastKnownHash { get; set; } = [];
    public DateTime LastSync { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public bool IsTrusted { get; set; }
    public bool IsBlocked { get; set; }
    public string BlockReason { get; set; } = string.Empty;



    public ICollection<User> Users { get; set; } = [];






    public byte[] IntegrityHash { get; set; } = [];

    public byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Id.ToByteArray());
        bw.Write(PublicKey);
        bw.Write(SignPublicKey);
        bw.Write(Encoding.UTF8.GetBytes(TlsCertFingerprint));
        bw.Write(LastKnownHash);
        bw.Write(LastSync.ToBinary());
        bw.Write(LastSeen.ToBinary());
        bw.Write(IsTrusted ? (byte)1 : (byte)0);
        bw.Write(IsBlocked ? (byte)1 : (byte)0);
        bw.Write(Encoding.UTF8.GetBytes(BlockReason));
        bw.Write(Users.Count);

        return Hashing.SHA512Hash(ms.ToArray());
    }

    public bool IsIntegrityValid() => Hashing.Verify(IntegrityHash, CalculateIntegrityHash());

    public void GenerateIngetiryHash() => IntegrityHash = CalculateIntegrityHash();
}
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Models;

public sealed class LocalDeviceIdentity : IntegrityCheckableBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public byte[] AgreementPrivateKeyBlob { get; set; } = [];
    public byte[] SignPrivateKeyBlob { get; set; } = [];
    public byte[] PFXCertificate { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(Id.ToByteArray());
        bw.Write(AgreementPrivateKeyBlob);
        bw.Write(SignPrivateKeyBlob);
        bw.Write(PFXCertificate);
        bw.Write(CreatedAt.ToUnixTimeMilliseconds());

        return Hashing.SHA256Hash(ms.ToArray());
    }
}

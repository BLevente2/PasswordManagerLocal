using PasswordManagerLocalBackend.Security;
using System.Text;

namespace PasswordManagerLocalBackend.Models;

public sealed class LocalDeviceIdentity : IntegrityCheckableBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public byte[] AgreementPrivateKeyBlob { get; set; } = [];
    public byte[] SignPrivateKeyBlob { get; set; } = [];
    public byte[] PFXCertificate { get; set; } = [];

    public string DeviceName { get; set; } = string.Empty;
    public bool IsSyncOn { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public override byte[] CalculateIntegrityHash()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        WriteIntegrityBase(bw);
        bw.Write(Encoding.UTF8.GetBytes(DeviceName ?? string.Empty));
        bw.Write(IsSyncOn);
        bw.Write(CreatedAt.ToUnixTimeMilliseconds());

        return Hashing.SHA256Hash(ms.ToArray());
    }


    public byte[] CalculateLegacyIntegrityHashWithoutDeviceName()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        WriteIntegrityBase(bw);
        bw.Write(IsSyncOn);
        bw.Write(CreatedAt.ToUnixTimeMilliseconds());

        return Hashing.SHA256Hash(ms.ToArray());
    }


    public byte[] CalculateLegacyIntegrityHashWithoutSyncFlagAndDeviceName()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        WriteIntegrityBase(bw);
        bw.Write(CreatedAt.ToUnixTimeMilliseconds());

        return Hashing.SHA256Hash(ms.ToArray());
    }


    private void WriteIntegrityBase(BinaryWriter bw)
    {
        bw.Write(Id.ToByteArray());
        bw.Write(AgreementPrivateKeyBlob);
        bw.Write(SignPrivateKeyBlob);
        bw.Write(PFXCertificate);
    }
}

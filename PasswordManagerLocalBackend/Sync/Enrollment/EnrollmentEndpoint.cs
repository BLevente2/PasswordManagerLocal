using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Sync.Enrollment;

internal sealed class EnrollmentEndpoint
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public Guid DeviceId { get; set; }
    public string TlsCertFingerprint { get; set; } = string.Empty;
    public byte[] SignPublicKey { get; set; } = [];
    public byte[] AgreementPublicKey { get; set; } = [];
    public DeviceType DeviceType { get; set; }
}

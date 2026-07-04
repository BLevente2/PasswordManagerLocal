using PasswordManagerLocal.Backend.Constants;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PasswordManagerLocal.Backend.Sync;

public sealed class DeviceEnrollmentParsedDirectEndpoint
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public Guid DeviceId { get; set; }
    public string TlsCertFingerprint { get; set; } = string.Empty;
    public byte[] SignPublicKey { get; set; } = [];
    public byte[] AgreementPublicKey { get; set; } = [];
}

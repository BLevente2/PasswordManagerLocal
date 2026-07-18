using PasswordManagerLocal.Backend.Constants;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Sync;

public sealed class DeviceEnrollmentDirectEndpointInfo
{
    public Guid DeviceId { get; set; }
    public Guid OriginInstanceId { get; set; }
    public string TlsCertFingerprint { get; set; } = string.Empty;
    public byte[] SignPublicKey { get; set; } = [];
    public byte[] AgreementPublicKey { get; set; } = [];
    public DeviceType DeviceType { get; set; }
    public int Port { get; set; }
    public IReadOnlyList<string> Hosts { get; set; } = [];
}

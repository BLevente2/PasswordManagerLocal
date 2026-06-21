using PasswordManagerLocalBackend.Constants;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PasswordManagerLocalBackend.Sync;

public sealed class DeviceEnrollmentParsedCode
{
    public string SessionId { get; set; } = string.Empty;
    public byte[] Secret { get; set; } = [];
    public List<DeviceEnrollmentParsedDirectEndpoint> DirectEndpoints { get; set; } = [];
}

using System.Net;

namespace PasswordManagerLocalBackend.Sync.Enrollment;

internal sealed class LocalEnrollmentHostCandidate
{
    public IPAddress Address { get; set; } = IPAddress.None;
    public int Priority { get; set; }
    public string InterfaceName { get; set; } = string.Empty;
    public string InterfaceDescription { get; set; } = string.Empty;
    public bool IsVirtualAdapter { get; set; }
    public bool HasGateway { get; set; }
}

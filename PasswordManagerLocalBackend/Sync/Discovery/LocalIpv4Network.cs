using System.Net;

namespace PasswordManagerLocalBackend.Sync.Discovery;

internal sealed class LocalIpv4Network
{
    public IPAddress Address { get; set; } = IPAddress.None;
    public IPAddress Mask { get; set; } = IPAddress.None;
    public bool IsVirtualAdapter { get; set; }
}

using System.Net;

namespace PasswordManagerLocalBackend.Services;

internal sealed class LocalIpv4Network
{
    public IPAddress Address { get; set; } = IPAddress.None;
    public IPAddress Mask { get; set; } = IPAddress.None;
    public string InterfaceName { get; set; } = string.Empty;
    public bool IsVirtualAdapter { get; set; }
}

using System.Net;

namespace PasswordManagerLocalBackend.Services.Hosted;

internal sealed class LocalIpv4Network
{
    public IPAddress Address { get; set; } = IPAddress.None;
    public IPAddress Mask { get; set; } = IPAddress.None;
    public bool IsVirtualAdapter { get; set; }
}

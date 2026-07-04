using System.Net;

namespace PasswordManagerLocal.Backend.Sync.Discovery;

internal sealed class LocalIpv4Network
{
    public IPAddress Address { get; init; } = IPAddress.None;
    public IPAddress Mask { get; init; } = IPAddress.None;
    public bool IsVirtualAdapter { get; init; }
}

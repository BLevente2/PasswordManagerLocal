using System.Net;

namespace PasswordManagerLocal.Backend.Sync.Discovery;

internal sealed class LocalDiscoveryDatagram
{
    public required byte[] Payload { get; init; }
    public required IPEndPoint RemoteEndpoint { get; init; }
}

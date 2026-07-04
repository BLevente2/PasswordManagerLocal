using PasswordManagerLocalBackend.Sync.Discovery;
using System.Net;

namespace PasswordManagerLocalBackend.Abstractions.Services;

internal interface ILocalDiscoveryTransport
{
    Task StartAsync(Func<LocalDiscoveryDatagram, CancellationToken, Task> receiveHandler, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task SendMulticastAsync(byte[] payload, CancellationToken ct = default);
    Task SendUnicastAsync(byte[] payload, IPEndPoint remoteEndpoint, CancellationToken ct = default);
}

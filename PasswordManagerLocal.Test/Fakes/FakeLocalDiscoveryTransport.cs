using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Sync.Discovery;
using System.Net;

namespace PasswordManagerLocal.Test.Fakes;

internal sealed class FakeLocalDiscoveryTransport : ILocalDiscoveryTransport
{
    private Func<LocalDiscoveryDatagram, CancellationToken, Task>? _receiveHandler;

    public List<byte[]> MulticastPayloads { get; } = [];
    public List<(byte[] Payload, IPEndPoint RemoteEndpoint)> UnicastPayloads { get; } = [];
    public bool IsStarted { get; private set; }

    public Task StartAsync(Func<LocalDiscoveryDatagram, CancellationToken, Task> receiveHandler, CancellationToken ct = default)
    {
        _receiveHandler = receiveHandler;
        IsStarted = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        IsStarted = false;
        return Task.CompletedTask;
    }

    public Task SendMulticastAsync(byte[] payload, CancellationToken ct = default)
    {
        MulticastPayloads.Add(payload.ToArray());
        return Task.CompletedTask;
    }

    public Task SendUnicastAsync(byte[] payload, IPEndPoint remoteEndpoint, CancellationToken ct = default)
    {
        UnicastPayloads.Add((payload.ToArray(), remoteEndpoint));
        return Task.CompletedTask;
    }

    public Task InjectAsync(byte[] payload, string sourceAddress = "192.168.1.50", int sourcePort = 26689, CancellationToken ct = default)
    {
        if (_receiveHandler is null)
            throw new InvalidOperationException("The fake discovery transport is not started.");

        return _receiveHandler(new LocalDiscoveryDatagram
        {
            Payload = payload.ToArray(),
            RemoteEndpoint = new IPEndPoint(IPAddress.Parse(sourceAddress), sourcePort)
        }, ct);
    }
}

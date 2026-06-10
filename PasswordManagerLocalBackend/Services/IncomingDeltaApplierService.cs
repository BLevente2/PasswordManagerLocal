using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Services;

public sealed class IncomingDeltaApplierService : IIncomingDeltaApplierService
{
    private readonly INetworkDeltaService _networkDeltas;

    public IncomingDeltaApplierService(INetworkDeltaService networkDeltas)
    {
        _networkDeltas = networkDeltas;
    }




    public Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default) =>
        _networkDeltas.ApplyAsync(delta, ct);
}

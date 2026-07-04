using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Services;

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

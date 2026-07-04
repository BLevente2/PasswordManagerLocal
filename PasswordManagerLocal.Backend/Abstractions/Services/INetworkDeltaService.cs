using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface INetworkDeltaService
{
    Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default);
    Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default);
}

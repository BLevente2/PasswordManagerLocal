using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface INetworkDeltaService
{
    Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default);
    Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default);
}

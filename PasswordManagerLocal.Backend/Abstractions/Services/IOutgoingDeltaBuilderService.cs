using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IOutgoingDeltaBuilderService
{
    Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default);
    Task<NetworkDelta> BuildUserSnapshotRelayAsync(UserSyncSnapshot snapshot, Device device, CancellationToken ct = default);
}

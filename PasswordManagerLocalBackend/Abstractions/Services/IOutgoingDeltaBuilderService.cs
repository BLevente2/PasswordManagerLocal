using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IOutgoingDeltaBuilderService
{
    Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default);
}

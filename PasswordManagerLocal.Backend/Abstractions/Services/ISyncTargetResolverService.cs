using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ISyncTargetResolverService
{
    Task<IReadOnlyList<Device>> ResolveTargetsAsync(
        SyncItem item,
        bool touchLocalSyncState,
        IReadOnlyCollection<Guid> excludedDeviceIds,
        CancellationToken ct = default);
}

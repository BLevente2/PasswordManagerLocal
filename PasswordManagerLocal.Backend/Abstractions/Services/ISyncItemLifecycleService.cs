using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ISyncItemLifecycleService
{
    Task<SyncItem> GetOrCreateAsync(SyncItem item, long changedAtTs, CancellationToken ct = default);
    Task TouchLocalStateAsync(SyncItem item, long changedAtTs, CancellationToken ct = default);
}

using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ISyncService
{
    Task NeedsSyncAsync(Guid modelId, SyncModelType modelType, SyncChangeType changeType, CancellationToken ct = default);
}
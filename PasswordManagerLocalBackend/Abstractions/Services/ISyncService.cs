using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ISyncService
{
    Task NeedsSyncAsync(Guid modelId, SyncModelType modelType, SyncChangeType changeType, CancellationToken ct = default);
}
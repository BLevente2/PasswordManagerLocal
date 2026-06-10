using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Services;

public sealed class SyncService : ISyncService
{
    private readonly ISyncQueueService _queue;

    public SyncService(ISyncQueueService queue)
    {
        _queue = queue;
    }




    public async Task NeedsSyncAsync(
        Guid modelId,
        SyncModelType modelType,
        SyncChangeType changeType,
        CancellationToken ct = default)
    {
        var item = new SyncItem
        {
            ModelId = modelId,
            ModelType = modelType,
            ChangeType = changeType
        };

        await _queue.EnqueueAsync(item, ct);
    }
}

using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalTest.Fakes;

public sealed class FakeSyncTombstoneRepository : ISyncTombstoneRepository
{
    private readonly Dictionary<(Guid ModelId, SyncModelType ModelType), SyncTombstone> _items = [];

    public IReadOnlyCollection<SyncTombstone> Items => _items.Values;

    public Task<SyncTombstone?> GetAsync(Guid modelId, SyncModelType modelType, CancellationToken ct = default) =>
        Task.FromResult(_items.GetValueOrDefault((modelId, modelType)));

    public Task UpsertAsync(Guid modelId, SyncModelType modelType, long deletedAtTs, CancellationToken ct = default)
    {
        _items[(modelId, modelType)] = new SyncTombstone
        {
            ModelId = modelId,
            ModelType = modelType,
            DeletedAtTs = deletedAtTs
        };
        return Task.CompletedTask;
    }

    public void Delete(SyncTombstone tombstone) =>
        _items.Remove((tombstone.ModelId, tombstone.ModelType));
}

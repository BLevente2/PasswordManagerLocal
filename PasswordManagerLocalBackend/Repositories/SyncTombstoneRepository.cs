using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class SyncTombstoneRepository : ISyncTombstoneRepository
{
    private readonly DbSet<SyncTombstone> _set;

    public SyncTombstoneRepository(AppDbContext context)
    {
        _set = context.SyncTombstones;
    }




    public async Task<SyncTombstone?> GetAsync(Guid modelId, SyncModelType modelType, CancellationToken ct = default) =>
        await _set.FirstOrDefaultAsync(x => x.ModelId == modelId && x.ModelType == modelType, ct);


    public async Task UpsertAsync(Guid modelId, SyncModelType modelType, long deletedAtTs, CancellationToken ct = default)
    {
        var existing = await GetAsync(modelId, modelType, ct);
        if (existing is not null)
        {
            if (deletedAtTs > existing.DeletedAtTs)
                existing.DeletedAtTs = deletedAtTs;

            return;
        }

        await _set.AddAsync(new SyncTombstone
        {
            ModelId = modelId,
            ModelType = modelType,
            DeletedAtTs = deletedAtTs
        }, ct);
    }


    public void Delete(SyncTombstone tombstone) =>
        _set.Remove(tombstone);
}

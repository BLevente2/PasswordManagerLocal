using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using static PasswordManagerLocalBackend.Constants.TombstoneConstants;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class SyncTombstoneRepository : ISyncTombstoneRepository
{
    private readonly AppDbContext _context;
    private readonly DbSet<SyncTombstone> _set;

    public SyncTombstoneRepository(AppDbContext context)
    {
        _context = context;
        _set = context.SyncTombstones;
    }




    public async Task<SyncTombstone?> GetAsync(Guid modelId, SyncModelType modelType, CancellationToken ct = default)
    {
        var local = _set.Local.FirstOrDefault(tombstone =>
            _context.Entry(tombstone).State != EntityState.Deleted &&
            tombstone.ModelId == modelId &&
            tombstone.ModelType == modelType);
        if (local is not null)
            return local;

        return await _set.FirstOrDefaultAsync(x => x.ModelId == modelId && x.ModelType == modelType, ct);
    }


    public async Task UpsertAsync(Guid modelId, SyncModelType modelType, long deletedAtTs, CancellationToken ct = default)
    {
        var existing = await GetAsync(modelId, modelType, ct);
        if (existing is not null)
        {
            if (deletedAtTs > existing.DeletedAtTs)
                existing.DeletedAtTs = deletedAtTs;

            return;
        }

        await RemoveOldestTombstonesToMakeRoomAsync(ct);

        await _set.AddAsync(new SyncTombstone
        {
            ModelId = modelId,
            ModelType = modelType,
            DeletedAtTs = deletedAtTs
        }, ct);
    }


    public void Delete(SyncTombstone tombstone) =>
        _set.Remove(tombstone);


    private async Task RemoveOldestTombstonesToMakeRoomAsync(CancellationToken ct)
    {
        if (MaxSyncTombstones < 1)
            return;

        var persisted = await _set
            .OrderBy(tombstone => tombstone.DeletedAtTs)
            .ThenBy(tombstone => tombstone.Id)
            .ToListAsync(ct);
        var tracked = _set.Local
            .Where(tombstone => _context.Entry(tombstone).State != EntityState.Deleted)
            .ToList();
        var candidates = persisted
            .Concat(tracked)
            .DistinctBy(tombstone => tombstone.Id)
            .OrderBy(tombstone => tombstone.DeletedAtTs)
            .ThenBy(tombstone => tombstone.Id)
            .ToList();

        var removeCount = candidates.Count - MaxSyncTombstones + 1;
        if (removeCount <= 0)
            return;

        _set.RemoveRange(candidates.Take(removeCount));
    }
}

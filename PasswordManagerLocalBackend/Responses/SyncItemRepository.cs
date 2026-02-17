using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;
using PasswordManagerLocalBackend.Repositories;

namespace PasswordManagerLocalBackend.Responses;

public sealed class SyncItemRepository : GenericRepositoryBase<SyncItem>, ISyncItemRepository
{
    public SyncItemRepository(AppDbContext context) : base(context.SyncItems)
    {

    }

    public override Task<SyncItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<int> ClearSyncItemsAsync(CancellationToken ct = default) =>
        await Set.Where(s => !s.QueueItems.Any())
        .ExecuteDeleteAsync(ct);

    public async Task<SyncItem?> GetAsync(Guid modelId, SyncModelType modelType, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(s => s.ModelId == modelId && s.ModelType == modelType, ct);
}
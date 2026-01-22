using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class SyncQueueRepository : GenericRepositoryBase<SyncItem>, ISyncQueueRepository
{
    public SyncQueueRepository(AppDbContext context) : base(context.SyncItems) { }

    public override async Task<SyncItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(s => s.Id == id, ct);
}
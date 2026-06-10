using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class GroupRepository : GenericRepositoryBase<Group>, IGroupRepository
{
    public GroupRepository(AppDbContext context) : base(context.Groups) { }

    public override async Task<Group?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<Group?> GetByIdWithUsersAsync(Guid id, CancellationToken ct = default) =>
        await Set
            .Include(g => g.Users)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<Group?> GetByIdAsNoTrackingWithUsersAsync(Guid id, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Include(g => g.Users)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
}
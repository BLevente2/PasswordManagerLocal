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
}
using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class UserRepository : GenericRepositoryBase<User>, IUserRepository
{
    public UserRepository(AppDbContext db) : base(db.Users) { }

    public override Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        Set.AsNoTracking().AnyAsync(u => u.UId == id, ct);

    public override Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(u => u.UId == id, ct);

    public Task<User?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken ct = default) =>
        Set.AsNoTracking().FirstOrDefaultAsync(u => u.UId == id, ct);

    public async Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return [];

        return await Set.Where(u => ids.Contains(u.UId)).ToListAsync(ct);
    }

    public Task<User?> GetByIdWithRelationsAsync(Guid id, CancellationToken ct = default) =>
        Set
            .Include(u => u.Groups)
            .Include(u => u.UserDevices)
            .Include(u => u.LocalUserDevices)
            .FirstOrDefaultAsync(u => u.UId == id, ct);

    public Task<User?> GetByIdAsNoTrackingWithRelationsAsync(Guid id, CancellationToken ct = default) =>
        Set.AsNoTracking()
            .Include(u => u.Groups)
            .Include(u => u.UserDevices)
            .Include(u => u.LocalUserDevices)
            .FirstOrDefaultAsync(u => u.UId == id, ct);

    public async Task<IReadOnlyList<User>> GetAllRememberMeEnabledUsersAsync(CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(u => u.SavedKey != null)
            .ToListAsync(ct);
}

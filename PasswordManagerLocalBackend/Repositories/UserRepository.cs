using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;

namespace PasswordManagerLocalBackend.Repositories;

public sealed class UserRepository : GenericRepositoryBase<User>, IUserRepository
{
    public UserRepository(AppDbContext db) : base(db.Users) { }

    public override async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await Set.FirstOrDefaultAsync(u => u.UId == id, ct);

    public async Task<User?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken ct = default) =>
        await Set.AsNoTracking().FirstOrDefaultAsync(u => u.UId == id, ct);

    public async Task<User?> GetByIdWithRelationsAsync(Guid id, CancellationToken ct = default) =>
        await Set
            .Include(u => u.Groups)
            .Include(u => u.Devices)
            .Include(u => u.UserDevices)
            .FirstOrDefaultAsync(u => u.UId == id, ct);

    public async Task<User?> GetByIdAsNoTrackingWithRelationsAsync(Guid id, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Include(u => u.Groups)
            .Include(u => u.Devices)
            .Include(u => u.UserDevices)
            .FirstOrDefaultAsync(u => u.UId == id, ct);

    public async Task<IReadOnlyList<User>> GetAllRememberMeEnabledUsersAsync(CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(u => u.SavedKey != null)
            .ToListAsync(ct);
}
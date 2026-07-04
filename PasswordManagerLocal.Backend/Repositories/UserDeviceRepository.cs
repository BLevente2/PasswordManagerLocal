using Microsoft.EntityFrameworkCore;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Persistence;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Repositories;

public sealed class UserDeviceRepository : IUserDeviceRepository
{
    private readonly DbSet<UserDevice> _set;

    public UserDeviceRepository(AppDbContext context)
    {
        _set = context.UserDevices;
    }

    public async Task<IReadOnlyList<UserDevice>> ListByUserAsync(Guid userId, CancellationToken ct = default) =>
        await _set.AsNoTracking()
            .Include(ud => ud.Device)
            .Where(ud => ud.UserId == userId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserDevice>> ListByDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        await _set.AsNoTracking()
            .Include(ud => ud.User)
            .Where(ud => ud.DeviceId == deviceId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UserDevice>> ListByUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return [];

        return await _set.AsNoTracking()
            .Include(ud => ud.Device)
            .Where(ud => userIds.Contains(ud.UserId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UserDevice>> ListActiveByDeviceAsync(Guid deviceId, CancellationToken ct = default)
    {
        var links = await _set.AsNoTracking()
            .Include(ud => ud.User)
            .Where(ud => ud.DeviceId == deviceId && !ud.IsDeleted)
            .ToListAsync(ct);
        return links;
    }

    public async Task<IReadOnlyList<UserDevice>> ListByUserIdsAndDeviceAsync(IReadOnlyCollection<Guid> userIds, Guid deviceId, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return [];

        return await _set
            .Include(ud => ud.User)
            .Where(ud => ud.DeviceId == deviceId && userIds.Contains(ud.UserId))
            .ToListAsync(ct);
    }

    public Task<UserDevice?> GetAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        _set
            .Include(ud => ud.Device)
            .Include(ud => ud.User)
            .FirstOrDefaultAsync(ud => ud.UserId == userId && ud.DeviceId == deviceId, ct);

    public Task<UserDevice?> GetByModelIdAsync(Guid modelId, CancellationToken ct = default) =>
        _set.Include(ud => ud.Device)
            .Include(ud => ud.User)
            .FirstOrDefaultAsync(ud => ud.ModelId == modelId, ct);

    public Task<bool> ExistsAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        _set.AsNoTracking().AnyAsync(ud => ud.UserId == userId && ud.DeviceId == deviceId, ct);

    public Task<bool> HasActiveLinkAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        _set.AsNoTracking().AnyAsync(ud => ud.UserId == userId && ud.DeviceId == deviceId && !ud.IsDeleted && ud.IsSyncOn, ct);

    public Task<bool> HasAnyActiveLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        _set.AsNoTracking().AnyAsync(ud => ud.DeviceId == deviceId && !ud.IsDeleted, ct);

    public Task<bool> HasAnyActiveSyncEnabledLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        _set.AsNoTracking().AnyAsync(ud => ud.DeviceId == deviceId && !ud.IsDeleted && ud.IsSyncOn, ct);

    public Task<bool> HasAnyDeletedLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        _set.AsNoTracking().AnyAsync(ud => ud.DeviceId == deviceId && ud.IsDeleted, ct);

    public Task<bool> HasAnyActiveLinkForDeviceExceptUserAsync(Guid deviceId, Guid userId, CancellationToken ct = default) =>
        _set.AsNoTracking().AnyAsync(ud => ud.DeviceId == deviceId && ud.UserId != userId && !ud.IsDeleted, ct);

    public Task<bool> SharesActiveUserAsync(Guid sourceDeviceId, Guid targetDeviceId, CancellationToken ct = default) =>
        _set.AsNoTracking().AnyAsync(target =>
            target.DeviceId == targetDeviceId && !target.IsDeleted &&
            _set.Any(source => source.DeviceId == sourceDeviceId && source.UserId == target.UserId && !source.IsDeleted && source.IsSyncOn), ct);

    public Task AddAsync(UserDevice userDevice, CancellationToken ct = default) =>
        _set.AddAsync(userDevice, ct).AsTask();

    public void Update(UserDevice userDevice) =>
        _set.Update(userDevice);

    public void Delete(UserDevice userDevice) =>
        _set.Remove(userDevice);

    private Task<List<UserDevice>> GetLinksAsync(
        System.Linq.Expressions.Expression<Func<UserDevice, bool>> predicate,
        CancellationToken ct) =>
        _set.AsNoTracking().Where(predicate).ToListAsync(ct);
}

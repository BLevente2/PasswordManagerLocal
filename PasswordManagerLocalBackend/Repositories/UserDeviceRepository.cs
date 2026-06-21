using Microsoft.EntityFrameworkCore;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Repositories;

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
            .Where(ud => ud.DeviceId == deviceId)
            .ToListAsync(ct);
        return links.Where(ud => !ud.IsDeleted).ToList();
    }

    public Task<UserDevice?> GetAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        _set
            .Include(ud => ud.Device)
            .Include(ud => ud.User)
            .FirstOrDefaultAsync(ud => ud.UserId == userId && ud.DeviceId == deviceId, ct);

    public async Task<UserDevice?> GetByModelIdAsync(Guid modelId, CancellationToken ct = default)
    {
        var userDevices = await _set
            .Include(ud => ud.Device)
            .Include(ud => ud.User)
            .ToListAsync(ct);
        return userDevices.FirstOrDefault(ud => SyncIdentityUtil.BuildUserDeviceModelId(ud.UserId, ud.DeviceId) == modelId);
    }

    public async Task<bool> ExistsAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        (await GetLinksAsync(ud => ud.UserId == userId && ud.DeviceId == deviceId, ct)).Count != 0;

    public async Task<bool> HasActiveLinkAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        (await GetLinksAsync(ud => ud.UserId == userId && ud.DeviceId == deviceId, ct))
        .Any(ud => !ud.IsDeleted && ud.IsSyncOn);

    public async Task<bool> HasAnyActiveLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        (await GetLinksAsync(ud => ud.DeviceId == deviceId, ct)).Any(ud => !ud.IsDeleted);

    public async Task<bool> HasAnyActiveSyncEnabledLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        (await GetLinksAsync(ud => ud.DeviceId == deviceId, ct)).Any(ud => !ud.IsDeleted && ud.IsSyncOn);

    public async Task<bool> HasAnyDeletedLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        (await GetLinksAsync(ud => ud.DeviceId == deviceId, ct)).Any(ud => ud.IsDeleted);

    public async Task<bool> HasAnyActiveLinkForDeviceExceptUserAsync(Guid deviceId, Guid userId, CancellationToken ct = default) =>
        (await GetLinksAsync(ud => ud.DeviceId == deviceId && ud.UserId != userId, ct)).Any(ud => !ud.IsDeleted);

    public async Task<bool> SharesActiveUserAsync(Guid sourceDeviceId, Guid targetDeviceId, CancellationToken ct = default)
    {
        var links = await GetLinksAsync(
            ud => ud.DeviceId == sourceDeviceId || ud.DeviceId == targetDeviceId,
            ct);

        var sourceUserIds = links
            .Where(ud => ud.DeviceId == sourceDeviceId && !ud.IsDeleted && ud.IsSyncOn)
            .Select(ud => ud.UserId)
            .ToHashSet();

        return sourceUserIds.Count != 0 && links.Any(ud =>
            ud.DeviceId == targetDeviceId &&
            !ud.IsDeleted &&
            sourceUserIds.Contains(ud.UserId));
    }

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

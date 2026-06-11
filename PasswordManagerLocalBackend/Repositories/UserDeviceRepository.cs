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


    public async Task<IReadOnlyList<UserDevice>> ListActiveByDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        await _set.AsNoTracking()
            .Include(ud => ud.User)
            .Where(ud => ud.DeviceId == deviceId && !ud.IsDeleted)
            .ToListAsync(ct);


    public async Task<UserDevice?> GetAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        await _set
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




    public async Task<UserDevice?> GetActiveByNameAsync(Guid userId, string name, Guid? exceptDeviceId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var normalizedName = name.Trim().ToUpper();

        return await _set
            .Include(ud => ud.Device)
            .Include(ud => ud.User)
            .FirstOrDefaultAsync(ud =>
                ud.UserId == userId &&
                !ud.IsDeleted &&
                ud.Name.ToUpper() == normalizedName &&
                (exceptDeviceId == null || ud.DeviceId != exceptDeviceId.Value), ct);
    }


    public async Task<bool> ExistsAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        await _set.AnyAsync(ud => ud.UserId == userId && ud.DeviceId == deviceId, ct);


    public async Task<bool> HasActiveLinkAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        await _set.AsNoTracking().AnyAsync(ud =>
            ud.UserId == userId &&
            ud.DeviceId == deviceId &&
            !ud.IsDeleted &&
            ud.IsSyncEnabled, ct);


    public async Task<bool> HasAnyActiveLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        await _set.AsNoTracking().AnyAsync(ud => ud.DeviceId == deviceId && !ud.IsDeleted, ct);


    public async Task<bool> HasAnyActiveSyncEnabledLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        await _set.AsNoTracking().AnyAsync(ud => ud.DeviceId == deviceId && !ud.IsDeleted && ud.IsSyncEnabled, ct);


    public async Task<bool> HasAnyDeletedLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        await _set.AsNoTracking().AnyAsync(ud => ud.DeviceId == deviceId && ud.IsDeleted, ct);


    public async Task<bool> HasAnyActiveLinkForDeviceExceptUserAsync(Guid deviceId, Guid userId, CancellationToken ct = default) =>
        await _set.AsNoTracking().AnyAsync(ud =>
            ud.DeviceId == deviceId &&
            ud.UserId != userId &&
            !ud.IsDeleted, ct);


    public async Task<bool> SharesActiveUserAsync(Guid sourceDeviceId, Guid targetDeviceId, CancellationToken ct = default)
    {
        var sourceUserIds = await _set.AsNoTracking()
            .Where(ud => ud.DeviceId == sourceDeviceId && !ud.IsDeleted && ud.IsSyncEnabled)
            .Select(ud => ud.UserId)
            .ToListAsync(ct);

        if (sourceUserIds.Count == 0)
            return false;

        return await _set.AsNoTracking().AnyAsync(ud =>
            ud.DeviceId == targetDeviceId &&
            !ud.IsDeleted &&
            sourceUserIds.Contains(ud.UserId), ct);
    }




    public async Task<bool> IsNameTakenAsync(Guid userId, string name, Guid? exceptDeviceId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalizedName = name.Trim().ToUpper();

        return await _set.AsNoTracking().AnyAsync(ud =>
            ud.UserId == userId &&
            !ud.IsDeleted &&
            ud.Name.ToUpper() == normalizedName &&
            (exceptDeviceId == null || ud.DeviceId != exceptDeviceId.Value), ct);
    }


    public Task AddAsync(UserDevice userDevice, CancellationToken ct = default) =>
        _set.AddAsync(userDevice, ct).AsTask();


    public void Update(UserDevice userDevice) =>
        _set.Update(userDevice);


    public void Delete(UserDevice userDevice) =>
        _set.Remove(userDevice);
}

using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalTest.Fakes;

public sealed class FakeUserDeviceRepository : IUserDeviceRepository
{
    private readonly List<UserDevice> _items = [];

    public Task<IReadOnlyList<UserDevice>> ListByUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<UserDevice>)_items.Where(x => x.UserId == userId).Select(Clone).ToList());

    public Task<IReadOnlyList<UserDevice>> ListByDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<UserDevice>)_items.Where(x => x.DeviceId == deviceId).Select(Clone).ToList());

    public Task<IReadOnlyList<UserDevice>> ListActiveByDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        Task.FromResult((IReadOnlyList<UserDevice>)_items.Where(x => x.DeviceId == deviceId && !x.IsDeleted).Select(Clone).ToList());

    public Task<UserDevice?> GetAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        Task.FromResult(_items.Where(x => x.UserId == userId && x.DeviceId == deviceId).Select(Clone).FirstOrDefault());

    public Task<UserDevice?> GetByModelIdAsync(Guid modelId, CancellationToken ct = default)
    {
        var result = _items
            .Where(x => PasswordManagerLocalBackend.Sync.SyncIdentityUtil.BuildUserDeviceModelId(x.UserId, x.DeviceId) == modelId)
            .Select(Clone)
            .FirstOrDefault();

        return Task.FromResult(result);
    }

    public Task<UserDevice?> GetActiveByNameAsync(Guid userId, string name, Guid? exceptDeviceId = null, CancellationToken ct = default)
    {
        var result = _items
            .Where(x => x.UserId == userId && !x.IsDeleted && x.Name == name && (!exceptDeviceId.HasValue || x.DeviceId != exceptDeviceId.Value))
            .Select(Clone)
            .FirstOrDefault();

        return Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        Task.FromResult(_items.Any(x => x.UserId == userId && x.DeviceId == deviceId));

    public Task<bool> HasActiveLinkAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        Task.FromResult(_items.Any(x => x.UserId == userId && x.DeviceId == deviceId && !x.IsDeleted));

    public Task<bool> HasAnyActiveLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        Task.FromResult(_items.Any(x => x.DeviceId == deviceId && !x.IsDeleted));

    public Task<bool> HasAnyActiveSyncEnabledLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        Task.FromResult(_items.Any(x => x.DeviceId == deviceId && !x.IsDeleted && x.IsSyncEnabled));

    public Task<bool> HasAnyDeletedLinkForDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        Task.FromResult(_items.Any(x => x.DeviceId == deviceId && x.IsDeleted));

    public Task<bool> HasAnyActiveLinkForDeviceExceptUserAsync(Guid deviceId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_items.Any(x => x.DeviceId == deviceId && x.UserId != userId && !x.IsDeleted));

    public Task<bool> SharesActiveUserAsync(Guid sourceDeviceId, Guid targetDeviceId, CancellationToken ct = default)
    {
        var sourceUsers = _items
            .Where(x => x.DeviceId == sourceDeviceId && !x.IsDeleted)
            .Select(x => x.UserId)
            .ToHashSet();

        var result = _items.Any(x => x.DeviceId == targetDeviceId && !x.IsDeleted && sourceUsers.Contains(x.UserId));
        return Task.FromResult(result);
    }

    public Task<bool> IsNameTakenAsync(Guid userId, string name, Guid? exceptDeviceId = null, CancellationToken ct = default) =>
        Task.FromResult(_items.Any(x => x.UserId == userId && !x.IsDeleted && x.Name == name && (!exceptDeviceId.HasValue || x.DeviceId != exceptDeviceId.Value)));

    public Task AddAsync(UserDevice userDevice, CancellationToken ct = default)
    {
        _items.RemoveAll(x => x.UserId == userDevice.UserId && x.DeviceId == userDevice.DeviceId);
        _items.Add(Clone(userDevice));
        return Task.CompletedTask;
    }

    public void Update(UserDevice userDevice)
    {
        _items.RemoveAll(x => x.UserId == userDevice.UserId && x.DeviceId == userDevice.DeviceId);
        _items.Add(Clone(userDevice));
    }

    public void Delete(UserDevice userDevice)
    {
        _items.RemoveAll(x => x.UserId == userDevice.UserId && x.DeviceId == userDevice.DeviceId);
    }

    private static UserDevice Clone(UserDevice item) =>
        new()
        {
            UserId = item.UserId,
            User = item.User,
            DeviceId = item.DeviceId,
            Device = item.Device,
            Name = item.Name,
            IsSyncEnabled = item.IsSyncEnabled,
            IsDeleted = item.IsDeleted,
            LinkedAt = item.LinkedAt,
            DeletedAt = item.DeletedAt,
            LastModifiedAt = item.LastModifiedAt
        };
}

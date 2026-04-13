using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using System.Security.Cryptography.X509Certificates;

namespace PasswordManagerLocalBackend.Services;

public sealed class SyncQueueService : ISyncQueueService
{
    private readonly ISyncQueueRepository _syncQueue;
    private readonly ISyncItemRepository _syncItems;
    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IDeviceRepository _devices;

    public SyncQueueService(ISyncQueueRepository syncQueue, ISyncItemRepository syncItems, IUserRepository users, IGroupRepository groups, IDeviceRepository devices)
    {
        _syncQueue = syncQueue; 
        _syncItems = syncItems;
        _users = users;
        _groups = groups;
        _devices = devices;
    }

    public async Task<bool> TryEnqueueAsync(SyncItem item, CancellationToken ct = default)
    {
        if (item.ModelType == SyncModelType.Device)
            throw new InvalidOperationException("Invalid sync queue enqueue method for device enqueue");

        try
        {
            await _syncItems.AddAsync(item);
        }
        catch
        {
            return false;
        }

        IReadOnlyList<Device> devices;
        if (item.ModelType == SyncModelType.User)
            devices = await _devices.ListUserDevicesAsync(item.ModelId, ct);
        else if (item.ModelType == SyncModelType.Group)
            devices = await _devices.ListGroupDevicesAsync(item.ModelId, ct);
        else
            return false;

        if (devices.Count == 0)
            return true;

        var queueItems = new List<SyncQueueItem>();
        foreach (var device in devices)
        {
            var queueItem = new SyncQueueItem
            {
                DeviceId = device.Id,
                SyncItemId = item.Id
            };
            queueItems.Add(queueItem);
        }
        await _syncQueue.EnqueueAsync(queueItems, ct);
        return true;
    }
}
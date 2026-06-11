using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Sync;
using System.Collections.Concurrent;

namespace PasswordManagerLocalBackend.Services;

public sealed class DeviceSyncTaskService : IDeviceSyncTaskService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncTransportClientService _syncTransport;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly IDeviceIdentityService _identity;
    private readonly ConcurrentDictionary<Guid, byte> _runningDeviceIds = new();
    private readonly ConcurrentDictionary<Guid, Task> _runningTasks = new();
    private readonly object _runtimeLock = new();
    private CancellationTokenSource _runtimeCancellation = new();

    public DeviceSyncTaskService(
        IServiceScopeFactory scopeFactory,
        ISyncTransportClientService syncTransport,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDiscoveredDeviceEndpointCache endpointCache,
        IDeviceIdentityService identity)
    {
        _scopeFactory = scopeFactory;
        _syncTransport = syncTransport;
        _syncDeviceIdentities = syncDeviceIdentities;
        _endpointCache = endpointCache;
        _identity = identity;
    }




    public bool TryStart(DiscoveredDeviceEndpoint endpoint, Device device)
    {
        if (!_identity.IsSyncOn)
            return false;

        if (device.Id == Guid.Empty)
            return false;

        if (!device.IsTrusted || device.IsBlocked)
            return false;

        if (IsLocalDevice(device))
            return false;

        if (string.IsNullOrWhiteSpace(endpoint.Host))
            return false;

        if (endpoint.Port <= 0)
            return false;

        if (string.IsNullOrWhiteSpace(endpoint.TlsCertFingerprint))
            return false;

        if (!_runningDeviceIds.TryAdd(device.Id, 0))
            return false;

        CancellationToken token;
        lock (_runtimeLock)
        {
            if (!_identity.IsSyncOn || _runtimeCancellation.IsCancellationRequested)
            {
                _runningDeviceIds.TryRemove(device.Id, out _);
                return false;
            }

            token = _runtimeCancellation.Token;
        }

        var targetDevice = CloneDevice(device);
        var task = Task.Run(() => RunAsync(endpoint, targetDevice, token), CancellationToken.None);
        _runningTasks[device.Id] = task;

        return true;
    }


    public async Task StopAllAsync(CancellationToken ct = default)
    {
        Task[] runningTasks;

        lock (_runtimeLock)
        {
            _runtimeCancellation.Cancel();
            runningTasks = _runningTasks.Values.ToArray();
        }

        if (runningTasks.Length != 0)
        {
            try
            {
                await Task.WhenAny(Task.WhenAll(runningTasks), Task.Delay(TimeSpan.FromSeconds(5), ct));
            }
            catch
            {
            }
        }

        _runningDeviceIds.Clear();
        _runningTasks.Clear();

        lock (_runtimeLock)
        {
            _runtimeCancellation.Dispose();
            _runtimeCancellation = new CancellationTokenSource();
        }
    }


    public void Dispose()
    {
        _runtimeCancellation.Cancel();
        _runtimeCancellation.Dispose();
    }


    private async Task RunAsync(DiscoveredDeviceEndpoint endpoint, Device targetDevice, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _identity.IsSyncOn)
            {
                var sent = await TrySendNextAsync(endpoint, targetDevice, ct);
                if (!sent)
                    break;
            }
        }
        catch
        {
        }
        finally
        {
            _runningDeviceIds.TryRemove(targetDevice.Id, out _);
            _runningTasks.TryRemove(targetDevice.Id, out _);

            try
            {
                if (!await HasPendingAsync(targetDevice.Id, CancellationToken.None))
                    _syncDeviceIdentities.TryRemove(targetDevice);
            }
            catch
            {
            }
        }
    }


    private async Task<bool> TrySendNextAsync(DiscoveredDeviceEndpoint endpoint, Device targetDevice, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        var queue = scope.ServiceProvider.GetRequiredService<ISyncQueueRepository>();
        var deltaBuilder = scope.ServiceProvider.GetRequiredService<IOutgoingDeltaBuilderService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        if (!_identity.IsSyncOn)
            return false;

        var queueItem = await queue.GetNextPendingForDeviceAsync(targetDevice.Id, ct);
        if (queueItem is null)
            return false;

        if (queueItem.SyncItem is null)
        {
            queue.Delete(queueItem);
            await uow.SaveChangesAsync(ct);
            return true;
        }

        var syncItem = queueItem.SyncItem;
        if (!await RefreshAndValidateTargetDeviceAsync(scope.ServiceProvider, targetDevice, syncItem, endpoint, ct))
            return false;

        NetworkDelta delta;
        try
        {
            delta = await deltaBuilder.BuildAsync(syncItem, targetDevice, ct);
        }
        catch (InvalidOperationException)
        {
            queue.Delete(queueItem);
            await uow.SaveChangesAsync(ct);
            return true;
        }
        catch (InvalidDataException)
        {
            queue.Delete(queueItem);
            await uow.SaveChangesAsync(ct);
            return true;
        }

        if (!_identity.IsSyncOn)
            return false;

        var sent = await _syncTransport.SendDeltasAsync(endpoint.Host, endpoint.Port, targetDevice.TlsCertFingerprint, [delta], ct);

        if (!sent)
        {
            _endpointCache.TryRemove(endpoint.TlsCertFingerprint);
            return false;
        }

        queue.Delete(queueItem);
        await uow.SaveChangesAsync(ct);

        await CleanupDetachedDeviceIfSyncCompletedAsync(scope.ServiceProvider, syncItem, ct);

        return true;
    }


    private async Task<bool> RefreshAndValidateTargetDeviceAsync(IServiceProvider services, Device targetDevice, SyncItem syncItem, DiscoveredDeviceEndpoint endpoint, CancellationToken ct)
    {
        var devices = services.GetRequiredService<IDeviceRepository>();
        var userDevices = services.GetRequiredService<IUserDeviceRepository>();

        var freshDevice = await devices.GetByIdWithUserDevicesAsync(targetDevice.Id, ct);
        if (!_identity.IsSyncOn || freshDevice is null || !freshDevice.IsTrusted || freshDevice.IsBlocked || IsLocalDevice(freshDevice))
        {
            _syncDeviceIdentities.TryRemove(targetDevice);
            return false;
        }

        if (freshDevice.PublicKey.Length == 0 ||
            freshDevice.SignPublicKey.Length == 0 ||
            string.IsNullOrWhiteSpace(freshDevice.TlsCertFingerprint) ||
            !string.Equals(NormalizeFingerprint(freshDevice.TlsCertFingerprint), NormalizeFingerprint(endpoint.TlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
        {
            _syncDeviceIdentities.TryRemove(freshDevice);
            return false;
        }

        if (!await userDevices.HasAnyActiveSyncEnabledLinkForDeviceAsync(freshDevice.Id, ct) &&
            !await IsFinalDisconnectDeltaForTargetAsync(userDevices, syncItem, freshDevice.Id, ct) &&
            !await IsFinalSyncDisableDeltaForTargetAsync(userDevices, syncItem, freshDevice.Id, ct))
        {
            _syncDeviceIdentities.TryRemove(freshDevice);
            return false;
        }

        targetDevice.PublicKey = freshDevice.PublicKey.ToArray();
        targetDevice.SignPublicKey = freshDevice.SignPublicKey.ToArray();
        targetDevice.TlsCertFingerprint = freshDevice.TlsCertFingerprint;
        targetDevice.LastKnownHash = freshDevice.LastKnownHash.ToArray();
        targetDevice.LastSync = freshDevice.LastSync;
        targetDevice.LastSeen = freshDevice.LastSeen;
        targetDevice.IsTrusted = freshDevice.IsTrusted;
        targetDevice.IsBlocked = freshDevice.IsBlocked;
        targetDevice.BlockedReason = freshDevice.BlockedReason;
        targetDevice.BlockedAt = freshDevice.BlockedAt;
        targetDevice.InvalidSyncAttemptCount = freshDevice.InvalidSyncAttemptCount;
        targetDevice.LastInvalidSyncAttemptAt = freshDevice.LastInvalidSyncAttemptAt;
        targetDevice.LastModifiedAt = freshDevice.LastModifiedAt;
        targetDevice.IntegrityHash = freshDevice.IntegrityHash.ToArray();

        return true;
    }




    private static async Task<bool> IsFinalDisconnectDeltaForTargetAsync(IUserDeviceRepository userDevices, SyncItem syncItem, Guid targetDeviceId, CancellationToken ct)
    {
        if (syncItem.ModelType != SyncModelType.UserDevice || syncItem.ChangeType != SyncChangeType.Deleted)
            return false;

        var userDevice = await userDevices.GetByModelIdAsync(syncItem.ModelId, ct);
        return userDevice is not null &&
               userDevice.DeviceId == targetDeviceId &&
               userDevice.IsDeleted;
    }


    private static async Task<bool> IsFinalSyncDisableDeltaForTargetAsync(IUserDeviceRepository userDevices, SyncItem syncItem, Guid targetDeviceId, CancellationToken ct)
    {
        if (syncItem.ModelType != SyncModelType.UserDevice || syncItem.ChangeType == SyncChangeType.Deleted)
            return false;

        var userDevice = await userDevices.GetByModelIdAsync(syncItem.ModelId, ct);
        return userDevice is not null &&
               userDevice.DeviceId == targetDeviceId &&
               !userDevice.IsDeleted &&
               !userDevice.IsSyncEnabled;
    }


    private async Task CleanupDetachedDeviceIfSyncCompletedAsync(IServiceProvider services, SyncItem syncItem, CancellationToken ct)
    {
        if (syncItem.ModelType != SyncModelType.UserDevice || syncItem.ChangeType != SyncChangeType.Deleted)
            return;

        var queue = services.GetRequiredService<ISyncQueueRepository>();
        if (await queue.HasPendingForSyncItemAsync(syncItem.Id, ct))
            return;

        var userDevices = services.GetRequiredService<IUserDeviceRepository>();
        var devices = services.GetRequiredService<IDeviceRepository>();
        var tombstones = services.GetRequiredService<ISyncTombstoneRepository>();
        var uow = services.GetRequiredService<IUnitOfWork>();

        var userDevice = await userDevices.GetByModelIdAsync(syncItem.ModelId, ct);
        if (userDevice is null || !userDevice.IsDeleted)
            return;

        if (await userDevices.HasAnyActiveLinkForDeviceAsync(userDevice.DeviceId, ct))
            return;

        var device = await devices.GetByIdWithUserDevicesAsync(userDevice.DeviceId, ct);
        if (device is null)
            return;

        await tombstones.UpsertAsync(syncItem.ModelId, SyncModelType.UserDevice, syncItem.ChangedAtTs, ct);
        _syncDeviceIdentities.TryRemove(device);
        devices.Delete(device);
        await uow.SaveChangesAsync(ct);
    }


    private async Task<bool> HasPendingAsync(Guid deviceId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<ISyncQueueRepository>();
        return await queue.HasPendingForDeviceAsync(deviceId, ct);
    }


    private bool IsLocalDevice(Device device)
    {
        if (!_identity.IsInitialized)
            return false;

        if (device.Id == _identity.LocalDeviceId)
            return true;

        if (device.SignPublicKey.SequenceEqual(_identity.SignPublicKey))
            return true;

        return string.Equals(NormalizeFingerprint(device.TlsCertFingerprint), NormalizeFingerprint(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase);
    }


    private static string NormalizeFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return string.Empty;

        return fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
    }


    private static Device CloneDevice(Device source) =>
        new()
        {
            Id = source.Id,
            PublicKey = source.PublicKey.ToArray(),
            SignPublicKey = source.SignPublicKey.ToArray(),
            TlsCertFingerprint = source.TlsCertFingerprint,
            LastKnownHash = source.LastKnownHash.ToArray(),
            LastSync = source.LastSync,
            LastSeen = source.LastSeen,
            IsTrusted = source.IsTrusted,
            IsBlocked = source.IsBlocked,
            BlockedReason = source.BlockedReason,
            BlockedAt = source.BlockedAt,
            InvalidSyncAttemptCount = source.InvalidSyncAttemptCount,
            LastInvalidSyncAttemptAt = source.LastInvalidSyncAttemptAt,
            IntegrityHash = source.IntegrityHash.ToArray(),
            LastModifiedAt = source.LastModifiedAt
        };
}

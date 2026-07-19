using PasswordManagerLocal.Backend.Abstractions.Caching;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using static PasswordManagerLocal.Backend.Utils.DataValidationUtil;
using static PasswordManagerLocal.Backend.Constants.SyncConstants;

namespace PasswordManagerLocal.Backend.Services;

public sealed class DeviceService : IDeviceService
{
    private readonly IUserLookupService _userLookup;
    private readonly IUserDataReaderService _userDataReader;
    private readonly IUserDataWriterService _userDataWriter;
    private readonly IAuthService _auth;
    private readonly IDeviceIdentityService _identity;
    private readonly IDeviceRepository _devices;
    private readonly IGroupRepository _groups;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ILocalUserDeviceRepository _localUserDevices;
    private readonly ISyncRouteRepository _syncRoutes;
    private readonly ISyncChangeQueueService _syncChanges;
    private readonly IUserSyncCatchUpService _userSyncCatchUp;
    private readonly ISyncQueueRepository _syncQueueItems;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly IUnitOfWork _uow;
    private readonly IUserLifecycleCoordinator _lifecycle;
    private readonly IUserSnapshotMergeCoordinator _snapshotMerge;
    private readonly IUserMembershipAuthorizationRepository _membershipAuthorizations;
    private readonly IUserRevisionKnowledgeRepository _revisionKnowledge;
    private readonly IUserControlOperationRepository _controlOperations;
    private readonly IUserControlStateRepository _controlStates;
    private readonly IUserControlOperationWriterService _controlWriter;
    private readonly IUserSnapshotPublisherService _snapshotPublisher;
    private readonly ISyncQueueWriterService _queueWriter;
    private readonly IPendingSyncActivationService _activation;
    private readonly IKeyVaultService _keyVault;
    private readonly IUserSyncSnapshotRepository _snapshots;
    private readonly ISyncVersionClockService _versionClock;
    private readonly IUserTombstoneGarbageCollector? _garbageCollector;

    public DeviceService(
        IUserLookupService userLookup,
        IUserDataReaderService userDataReader,
        IUserDataWriterService userDataWriter,
        IAuthService auth,
        IDeviceIdentityService identity,
        IDeviceRepository devices,
        IGroupRepository groups,
        IUserDeviceRepository userDevices,
        ILocalUserDeviceRepository localUserDevices,
        ISyncRouteRepository syncRoutes,
        ISyncChangeQueueService syncChanges,
        IUserSyncCatchUpService userSyncCatchUp,
        ISyncQueueRepository syncQueueItems,
        ISyncDeviceIdentityService syncDeviceIdentities,
        ISyncRuntimeService syncRuntime,
        IDiscoveredDeviceEndpointCache endpointCache,
        IUnitOfWork uow,
        IUserLifecycleCoordinator lifecycle,
        IUserSnapshotMergeCoordinator snapshotMerge,
        IUserMembershipAuthorizationRepository membershipAuthorizations,
        IUserRevisionKnowledgeRepository revisionKnowledge,
        IUserControlOperationRepository controlOperations,
        IUserControlStateRepository controlStates,
        IUserControlOperationWriterService controlWriter,
        IUserSnapshotPublisherService snapshotPublisher,
        ISyncQueueWriterService queueWriter,
        IPendingSyncActivationService activation,
        IKeyVaultService keyVault,
        IUserSyncSnapshotRepository snapshots,
        ISyncVersionClockService versionClock,
        IUserTombstoneGarbageCollector? garbageCollector = null)
    {
        _userLookup = userLookup;
        _userDataReader = userDataReader;
        _userDataWriter = userDataWriter;
        _auth = auth;
        _identity = identity;
        _devices = devices;
        _groups = groups;
        _userDevices = userDevices;
        _localUserDevices = localUserDevices;
        _syncRoutes = syncRoutes;
        _syncChanges = syncChanges;
        _userSyncCatchUp = userSyncCatchUp;
        _syncQueueItems = syncQueueItems;
        _syncDeviceIdentities = syncDeviceIdentities;
        _syncRuntime = syncRuntime;
        _endpointCache = endpointCache;
        _uow = uow;
        _lifecycle = lifecycle;
        _snapshotMerge = snapshotMerge;
        _membershipAuthorizations = membershipAuthorizations;
        _revisionKnowledge = revisionKnowledge;
        _controlOperations = controlOperations;
        _controlStates = controlStates;
        _controlWriter = controlWriter;
        _snapshotPublisher = snapshotPublisher;
        _queueWriter = queueWriter;
        _activation = activation;
        _keyVault = keyVault;
        _snapshots = snapshots;
        _versionClock = versionClock;
        _garbageCollector = garbageCollector;
    }

    public Task<LocalDeviceInfoResponse> GetLocalDeviceInfoAsync(CancellationToken ct = default) =>
        Task.FromResult(new LocalDeviceInfoResponse
        {
            DeviceId = _identity.LocalDeviceId,
            TlsCertFingerprint = _identity.FingerprintHex,
            DeviceType = _identity.DeviceType,
            IsSyncOn = _identity.IsSyncOn,
            CreatedAt = _identity.CreatedAt
        });

    public async Task<bool> GetLocalUserSyncOnAsync(Guid token, CancellationToken ct = default)
    {
        var user = await _userLookup.GetAndVerifyUserAsync(token, ct);
        var link = await EnsureLocalUserDeviceAsync(user.UId, ct);
        return link.IsSyncOn;
    }

    public async Task SetLocalUserSyncOnAsync(Guid token, bool isSyncOn, CancellationToken ct = default)
    {
        var user = await _userLookup.GetAndVerifyUserAsync(token, ct);
        var link = await EnsureLocalUserDeviceAsync(user.UId, ct);
        if (link.IsSyncOn == isSyncOn)
            return;

        link.IsSyncOn = isSyncOn;
        link.GenerateIntegrityHash();
        _localUserDevices.Update(link);
        await _uow.SaveChangesAsync(ct);
        await _syncRuntime.RefreshSyncEnabledAsync(ct);

        if (isSyncOn)
        {
            var remotes = await _userDevices.ListByUserAsync(user.UId, ct);
            foreach (var deleted in remotes.Where(x => x.IsDeleted))
            {
                await _syncChanges.EnqueueForDeviceAsync(new SyncItem
                {
                    ModelId = SyncIdentityUtil.BuildUserDeviceModelId(deleted.UserId, deleted.DeviceId),
                    ModelType = SyncModelType.UserDevice,
                    ChangeType = SyncChangeType.Deleted,
                    ChangedAtTs = deleted.LastModifiedAt.ToUnixTimeMilliseconds()
                }, deleted.DeviceId, ct);
            }

            foreach (var remote in remotes.Where(x => !x.IsDeleted && x.IsSyncOn))
                await _userSyncCatchUp.EnqueueAsync(user.UId, remote.DeviceId, ct);
        }
    }

    public Task SetLocalDeviceNameAsync(Guid token, string name, CancellationToken ct = default) =>
        SetEncryptedDeviceNameAsync(token, _identity.LocalDeviceId, name, ct);

    public async Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default)
    {
        var user = await _userLookup.GetAndVerifyUserAsync(token, ct);
        var bundle = await _userDataReader.GetLoadAndVerifyUserDataBundleAsync(token, ct, user);
        var userDevicesData = bundle.UserDevicesData;
        var localLink = await EnsureLocalUserDeviceAsync(user.UId, ct);
        var links = await _userDevices.ListByUserWithDevicesAsync(user.UId, ct);

        var changed = EnsureEncryptedDeviceData(userDevicesData, _identity.LocalDeviceId, DateTimeOffset.UtcNow);
        foreach (var link in links.Where(x => !x.IsDeleted))
            changed |= EnsureEncryptedDeviceData(userDevicesData, link.DeviceId, link.LastModifiedAt);
        if (changed)
            await PersistUserDeviceDataAsync(bundle, token, ct);

        var encryptedDevices = userDevicesData.Devices.ToDictionary(d => d.Id);
        var visibleDeviceIds = links
            .Where(link => !link.IsDeleted)
            .Select(link => link.DeviceId)
            .Append(_identity.LocalDeviceId)
            .ToHashSet();
        var presentationNames = UserDevicePresentationUtil.ResolveNames(
            userDevicesData.Devices.Where(device => visibleDeviceIds.Contains(device.Id)));
        var result = new List<UserDeviceInfoResponse>();
        var localCanSync = localLink.IsSyncOn && _identity.IsSyncOn;
        if (encryptedDevices.TryGetValue(_identity.LocalDeviceId, out var localDeviceData))
            result.Add(BuildLocalResponse(
                localLink,
                localDeviceData,
                presentationNames[_identity.LocalDeviceId],
                localCanSync));

        foreach (var link in links.Where(x => !x.IsDeleted && x.Device is not null))
        {
            if (encryptedDevices.TryGetValue(link.DeviceId, out var deviceData))
                result.Add(BuildRemoteResponse(
                    link,
                    link.Device!,
                    deviceData,
                    presentationNames[link.DeviceId],
                    IsRemoteDeviceOnline(localCanSync, link, link.Device!)));
        }

        return result
            .OrderByDescending(d => d.IsCurrentDevice)
            .ThenByDescending(d => d.LastSync ?? d.LastSeen ?? DateTime.MinValue)
            .ToList();
    }

    public Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default) =>
        SetEncryptedDeviceNameAsync(token, deviceId, name, ct);

    public async Task SetUserDeviceSyncOnAsync(Guid token, Guid deviceId, bool isSyncOn, CancellationToken ct = default)
    {
        if (deviceId == _identity.LocalDeviceId)
        {
            await SetLocalUserSyncOnAsync(token, isSyncOn, ct);
            return;
        }

        var user = await _userLookup.GetAndVerifyUserAsync(token, ct);
        var userDevice = await GetActiveRemoteUserDeviceAsync(user.UId, deviceId, ct);
        if (userDevice.IsSyncOn == isSyncOn)
        {
            if (!isSyncOn)
                await RemoveCachedDeviceIfNoPendingAsync(userDevice, ct);
            return;
        }

        userDevice.IsSyncOn = isSyncOn;
        userDevice.LastModifiedAt = DateTimeOffset.UtcNow;
        userDevice.GenerateIntegrityHash();
        _userDevices.Update(userDevice);
        await EnqueueUserDeviceChangeAsync(userDevice, SyncChangeType.Updated, ct);

        if (isSyncOn)
            await _userSyncCatchUp.EnqueueAsync(user.UId, deviceId, ct);
        else
            await RemoveCachedDeviceIfNoPendingAsync(userDevice, ct);
    }

    public async Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default)
    {
        var user = await _userLookup.GetAndVerifyUserAsync(token, ct);
        await GetActiveRemoteUserDeviceAsync(user.UId, deviceId, ct);
        var device = await _devices.GetByIdWithUserDevicesAsync(deviceId, ct) ?? throw new InvalidInputException();
        if (!device.IsBlocked && device.InvalidSyncAttemptCount == 0 && device.BlockedReason is null)
            return;

        device.IsBlocked = false;
        device.BlockedReason = null;
        device.BlockedAt = null;
        device.InvalidSyncAttemptCount = 0;
        device.LastInvalidSyncAttemptAt = null;
        device.LastModifiedAt = DateTimeOffset.UtcNow;
        device.GenerateIntegrityHash();
        _devices.Update(device);
        await _syncChanges.EnqueueAsync(new SyncItem { ModelId = device.Id, ModelType = SyncModelType.Device, ChangeType = SyncChangeType.Updated }, ct);
    }

    public Task<DeviceRemovalResultResponse> DisconnectUserDeviceAsync(Guid token, Guid deviceId, byte[] masterPassword, CancellationToken ct = default)
    {
        if (!IsValidPassword(masterPassword) || deviceId == _identity.LocalDeviceId)
            throw new InvalidInputException();
        if (!_keyVault.TryGetEncryptionKey(token, out var key))
            throw new InvalidTokenException();
        return RemoveUnderLifecycleAsync(token, deviceId, masterPassword, key, ct);
    }

    private async Task<DeviceRemovalResultResponse> RemoveUnderLifecycleAsync(Guid token, Guid deviceId, byte[] masterPassword, PasswordManagerLocal.Backend.Security.EncryptionKey key, CancellationToken ct)
    {
        using (key)
        {
            var userId = (await _userLookup.GetAndVerifyUserAsync(token, ct)).UId;
            return await _lifecycle.ExecuteAsync(userId, async lifecycleToken =>
            {
                var user = await _userLookup.GetAndVerifyUserAsync(token, lifecycleToken);
                if (!_auth.IsPasswordValid(token, masterPassword, user.PasswordSalt))
                    throw new InvalidInputException();
                await GetActiveRemoteUserDeviceAsync(user.UId, deviceId, lifecycleToken);

                var controlState = await _controlStates.GetAsync(user.UId, lifecycleToken);
                if (controlState?.HasConflict == true)
                    throw new InvalidOperationException(controlState.ConflictReason ?? "The account control plane is quarantined.");
                if (await _snapshotsHasQuarantine(user, lifecycleToken))
                    throw new InvalidOperationException("Device removal is blocked by unresolved snapshot fork evidence.");

                await _snapshotMerge.TryMergePendingUnderLifecycleAsync(user.UId, key, UserSyncKeyConfidence.AuthenticatedSession, lifecycleToken);
                user = await _userLookup.GetAndVerifyUserAsync(token, lifecycleToken);
                var activeAuthorizations = await _membershipAuthorizations.ListActiveForDeviceAsync(user.UId, deviceId, lifecycleToken);
                if (activeAuthorizations.Count == 0)
                    throw new InvalidInputException();

                var cutoffRows = new List<DeviceRemovalOriginCutoffPayload>();
                var allKnownMerged = true;
                foreach (var authorization in activeAuthorizations)
                {
                    var knowledge = await _revisionKnowledge.ListForOriginAsync(user.UId, deviceId, authorization.OriginInstanceId, lifecycleToken);
                    var knowledgeByEpoch = knowledge.ToDictionary(item => item.UserKeyEpoch);
                    var highestControlSequence = await _controlOperations.GetHighestOriginSequenceAsync(user.UId, deviceId, authorization.OriginInstanceId, lifecycleToken);
                    var maximumKeyEpoch = authorization.MaximumKeyEpoch ?? user.KeyEpoch;
                    if (maximumKeyEpoch < authorization.MinimumKeyEpoch || maximumKeyEpoch > user.KeyEpoch)
                        throw new InvalidOperationException("The installation authorization key-epoch range is invalid.");

                    for (var keyEpoch = authorization.MinimumKeyEpoch; keyEpoch <= maximumKeyEpoch; keyEpoch = checked(keyEpoch + 1))
                    {
                        knowledgeByEpoch.TryGetValue(keyEpoch, out var item);
                        if (item is not null)
                            allKnownMerged &= item.HighestMergedRevision >= item.HighestStoredRevision;
                        cutoffRows.Add(new DeviceRemovalOriginCutoffPayload
                        {
                            AuthorizationId = authorization.AuthorizationId,
                            OriginInstanceId = authorization.OriginInstanceId,
                            UserKeyEpoch = keyEpoch,
                            HighestAcceptedSnapshotRevision = item?.HighestStoredRevision ?? 0,
                            HighestAcceptedControlSequence = highestControlSequence,
                            SignPublicKeyHash = authorization.SignPublicKeyHash.ToArray(),
                            AdditionOperationId = authorization.AdditionOperationId,
                            AdditionOperationHash = authorization.AdditionOperationHash?.ToArray()
                        });
                        if (keyEpoch == long.MaxValue)
                            break;
                    }
                }

                var payload = new DeviceRemovalPayload
                {
                    UserId = user.UId,
                    RemovedDeviceId = deviceId,
                    PreviousMembershipEpoch = user.MembershipEpoch,
                    ResultingMembershipEpoch = checked(user.MembershipEpoch + 1),
                    KeyEpoch = user.KeyEpoch,
                    Origins = cutoffRows
                };
                UserControlOperationEnvelopeUtil.FinalizeDeviceRemovalPayload(payload);

                await using var transaction = await _uow.BeginTransactionAsync(lifecycleToken);
                try
                {
                    var bundle = await _userDataReader.GetLoadAndVerifyUserDataBundleAsync(token, lifecycleToken, user);
                    var now = DateTimeOffset.UtcNow;
                    var encryptedDevice = bundle.UserDevicesData.Devices.FirstOrDefault(item => item.Id == deviceId);
                    if (encryptedDevice is not null)
                    {
                        TombstoneCleanupUtil.AddOrUpdateDeletedUserDevice(bundle.UserDevicesData, encryptedDevice.Id, now, _versionClock.Next());
                        encryptedDevice.Dispose();
                        bundle.UserDevicesData.Devices.Remove(encryptedDevice);
                        await _userDataWriter.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Devices, false, lifecycleToken);
                        user = await _userLookup.GetAndVerifyUserAsync(token, lifecycleToken);
                    }

                    var envelope = await _controlWriter.CreateAppliedDeviceRemovalUnderLifecycleAsync(user, payload, lifecycleToken);
                    user = await _userLookup.GetAndVerifyUserAsync(token, lifecycleToken);
                    var localSnapshot = await _snapshotPublisher.GetOrCreateAsync(user, lifecycleToken);
                    await _queueWriter.EnqueueAsync(new SyncItem
                    {
                        ModelId = user.UId,
                        ModelType = SyncModelType.User,
                        ChangeType = SyncChangeType.Updated,
                        ChangedAtTs = localSnapshot.CreatedAtUtc.ToUnixTimeMilliseconds()
                    }, localSnapshot.CreatedAtUtc.ToUnixTimeMilliseconds(), [deviceId], true, false, lifecycleToken);
                    await RemovePendingSyncsForUserToDeviceAsync(user.UId, deviceId, lifecycleToken);
                    await _uow.SaveChangesAsync(lifecycleToken);
                    await transaction.CommitAsync(lifecycleToken);
                    if (_garbageCollector is not null)
                    {
                        try { await _garbageCollector.CollectAsync(user.UId, key, CancellationToken.None); } catch { }
                    }
                    try { await _activation.ActivatePendingAsync(CancellationToken.None); } catch { }
                    return new DeviceRemovalResultResponse
                    {
                        Removed = true,
                        AllKnownOriginRevisionsMerged = allKnownMerged,
                        MayContainUnobservedChanges = true,
                        Message = allKnownMerged
                            ? "Removal completed. All revisions known to this installation were merged, but the removed device may still contain changes that were never observed here."
                            : "Removal completed with a signed cutoff, but some known origin revisions were not proven merged and the removed device may contain unseen changes.",
                        OperationId = envelope.OperationId,
                        ResultingMembershipEpoch = envelope.ResultingMembershipEpoch
                    };
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    _uow.ClearTrackedChanges();
                    throw;
                }
            }, ct);
        }
    }

    private async Task<bool> _snapshotsHasQuarantine(User user, CancellationToken ct) =>
        (await _snapshots.ListForUserAsync(user.UId, ct))
            .Any(snapshot => snapshot.UserKeyEpoch == user.KeyEpoch && snapshot.Status == UserSyncSnapshotStatus.Quarantined);

    private async Task SetEncryptedDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct)
    {
        var normalizedName = NormalizeUserDeviceName(name);
        var user = await _userLookup.GetAndVerifyUserAsync(token, ct);
        var bundle = await _userDataReader.GetLoadAndVerifyUserDataBundleAsync(token, ct, user);
        var userDevicesData = bundle.UserDevicesData;
        await EnsureLocalUserDeviceAsync(user.UId, ct);
        UserDevice? remoteLink = null;
        if (deviceId != _identity.LocalDeviceId)
            remoteLink = await GetActiveRemoteUserDeviceAsync(user.UId, deviceId, ct);

        if (userDevicesData.Devices.Any(d => d.Id != deviceId && string.Equals(d.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidInputException();

        var encryptedDevice = userDevicesData.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (encryptedDevice is null)
        {
            var version = _versionClock.Next();
            encryptedDevice = new UserDeviceData
            {
                Id = deviceId,
                Name = normalizedName,
                LinkedAt = remoteLink?.LastModifiedAt ?? DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                Version = version
            };
            userDevicesData.DeletedDevices.RemoveAll(deleted => deleted.Id == encryptedDevice.Id);
            userDevicesData.Devices.Add(encryptedDevice);
        }
        else if (string.Equals(encryptedDevice.Name, normalizedName, StringComparison.Ordinal))
            return;
        else
        {
            encryptedDevice.Name = normalizedName;
            encryptedDevice.LastUpdatedAt = DateTimeOffset.UtcNow;
            encryptedDevice.Version = _versionClock.Next();
        }

        encryptedDevice.GenerateIntegrityHash();
        await PersistUserDeviceDataAsync(bundle, token, ct);
    }

    private async Task<LocalUserDevice> EnsureLocalUserDeviceAsync(Guid userId, CancellationToken ct)
    {
        var link = await _localUserDevices.GetAsync(userId, ct);
        if (link is not null)
            return link;
        link = new LocalUserDevice
        {
            UserId = userId,
            LocalDeviceIdentityId = _identity.LocalDeviceId,
            IsSyncOn = true
        };
        link.GenerateIntegrityHash();
        await _localUserDevices.AddAsync(link, ct);
        await _uow.SaveChangesAsync(ct);
        await _syncRuntime.RefreshSyncEnabledAsync(ct);
        return link;
    }

    private bool EnsureEncryptedDeviceData(UserDevicesData userDevicesData, Guid deviceId, DateTimeOffset linkedAt)
    {
        if (userDevicesData.Devices.Any(d => d.Id == deviceId))
            return false;

        var baseName = DeviceNameUtil.BuildDefaultDeviceName(deviceId);
        var version = _versionClock.Next();
        var deviceData = new UserDeviceData
        {
            Id = deviceId,
            Name = BuildUniqueEncryptedDeviceName(userDevicesData, baseName, deviceId),
            LinkedAt = linkedAt == default ? DateTimeOffset.UtcNow : linkedAt,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Version = version
        };
        deviceData.GenerateIntegrityHash();
        userDevicesData.DeletedDevices.RemoveAll(deleted => deleted.Id == deviceData.Id);
        userDevicesData.Devices.Add(deviceData);
        return true;
    }

    private string BuildUniqueEncryptedDeviceName(UserDevicesData userDevicesData, string requestedName, Guid deviceId)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? DeviceNameUtil.BuildDefaultDeviceName(deviceId) : requestedName.Trim();
        if (!IsEncryptedNameTaken(userDevicesData, baseName, deviceId)) return baseName;
        for (var i = 2; i < 100; i++)
        {
            var suffix = $"-{i}";
            var candidate = baseName[..Math.Min(baseName.Length, 64 - suffix.Length)] + suffix;
            if (!IsEncryptedNameTaken(userDevicesData, candidate, deviceId)) return candidate;
        }
        throw new InvalidInputException();
    }

    private bool IsEncryptedNameTaken(UserDevicesData userDevicesData, string name, Guid exceptDeviceId) =>
        userDevicesData.Devices.Any(d => d.Id != exceptDeviceId && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    private Task PersistUserDeviceDataAsync(UserDataBundle bundle, Guid token, CancellationToken ct, bool enqueueSync = true) =>
        _userDataWriter.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Devices, enqueueSync, ct);

    private UserDeviceInfoResponse BuildLocalResponse(
        LocalUserDevice link,
        UserDeviceData deviceData,
        string presentationName,
        bool isOnline) => new()
    {
        DeviceId = _identity.LocalDeviceId,
        Name = presentationName,
        DeviceType = _identity.DeviceType,
        TlsCertFingerprint = _identity.FingerprintHex,
        LastSync = null,
        LastSeen = null,
        LastLoginDate = ToMeaningfulUtc(deviceData.LastLoginDate),
        IsTrusted = true,
        IsBlocked = false,
        InvalidSyncAttemptCount = 0,
        IsSyncOn = link.IsSyncOn,
        IsOnline = isOnline,
        IsDeleted = false,
        LinkedAt = UtcDateTimeUtil.ToUtc(deviceData.LinkedAt),
        DeletedAt = null,
        IsCurrentDevice = true
    };

    private UserDeviceInfoResponse BuildRemoteResponse(
        UserDevice link,
        Device device,
        UserDeviceData deviceData,
        string presentationName,
        bool isOnline) => new()
    {
        DeviceId = link.DeviceId,
        Name = presentationName,
        DeviceType = device.DeviceType,
        TlsCertFingerprint = device.TlsCertFingerprint,
        LastSync = ToMeaningfulUtc(device.LastSync),
        LastSeen = ToMeaningfulUtc(device.LastSeen),
        LastLoginDate = ToMeaningfulUtc(deviceData.LastLoginDate),
        IsTrusted = device.IsTrusted,
        IsBlocked = device.IsBlocked,
        BlockedReason = device.BlockedReason,
        BlockedAt = UtcDateTimeUtil.ToUtc(device.BlockedAt),
        InvalidSyncAttemptCount = device.InvalidSyncAttemptCount,
        IsSyncOn = link.IsSyncOn,
        IsOnline = isOnline,
        IsDeleted = link.IsDeleted,
        LinkedAt = UtcDateTimeUtil.ToUtc(deviceData.LinkedAt),
        DeletedAt = UtcDateTimeUtil.ToUtc(link.DeletedAt),
        IsCurrentDevice = false
    };

    private static DateTime? ToMeaningfulUtc(DateTime value) =>
        value == default || value == UtcDateTimeUtil.MinDateTime
            ? null
            : UtcDateTimeUtil.ToUtc(value);

    private bool IsRemoteDeviceOnline(bool localCanSync, UserDevice link, Device device) =>
        localCanSync &&
        link.IsSyncOn &&
        !link.IsDeleted &&
        device.IsTrusted &&
        !device.IsBlocked &&
        device.PublicKey.Length != 0 &&
        device.SignPublicKey.Length != 0 &&
        _endpointCache.IsRecentlyDiscovered(
            device.TlsCertFingerprint,
            TimeSpan.FromSeconds(LocalDiscoveryOnlineTimeoutSeconds));

    private Task EnqueueUserDeviceChangeAsync(UserDevice userDevice, SyncChangeType changeType, CancellationToken ct) =>
        _syncChanges.EnqueueAsync(new SyncItem
        {
            ModelId = SyncIdentityUtil.BuildUserDeviceModelId(userDevice.UserId, userDevice.DeviceId),
            ModelType = SyncModelType.UserDevice,
            ChangeType = changeType
        }, ct);

    private async Task RemovePendingSyncsForUserToDeviceAsync(Guid userId, Guid targetDeviceId, CancellationToken ct)
    {
        var pendingItems = await _syncQueueItems.ListPendingForDeviceWithItemsAsync(targetDeviceId, ct);
        foreach (var queueItem in pendingItems)
        {
            if (queueItem.SyncItem is not null && await IsSyncItemOnlyForRemovedUserOrRouteAsync(queueItem.SyncItem, userId, targetDeviceId, ct))
                _syncQueueItems.Delete(queueItem);
        }
    }

    private async Task<bool> IsSyncItemOnlyForRemovedUserOrRouteAsync(SyncItem item, Guid removedUserId, Guid targetDeviceId, CancellationToken ct)
    {
        if (item.ModelType == SyncModelType.User)
            return item.ModelId == removedUserId;

        if (item.ModelType == SyncModelType.UserDevice)
        {
            var link = await _userDevices.GetByModelIdAsync(item.ModelId, ct);
            return link?.UserId == removedUserId;
        }

        if (item.ModelType == SyncModelType.Group)
        {
            var userIds = await _groups.ListUserIdsAsync(item.ModelId, ct);
            if (!userIds.Contains(removedUserId))
                return false;

            return !await AnyOtherUserCanStillSyncToTargetAsync(userIds, removedUserId, targetDeviceId, ct);
        }

        if (item.ModelType == SyncModelType.Device)
        {
            var links = await _userDevices.ListByDeviceAsync(item.ModelId, ct);
            if (links.All(link => link.UserId != removedUserId))
                return false;

            return !await AnyOtherUserCanStillSyncToTargetAsync(
                links.Where(link => !link.IsDeleted && link.IsSyncOn).Select(link => link.UserId),
                removedUserId,
                targetDeviceId,
                ct);
        }

        return false;
    }

    private Task<bool> AnyOtherUserCanStillSyncToTargetAsync(
        IEnumerable<Guid> userIds,
        Guid removedUserId,
        Guid targetDeviceId,
        CancellationToken ct)
    {
        var candidateUserIds = userIds
            .Where(id => id != Guid.Empty && id != removedUserId)
            .Distinct()
            .ToArray();
        return _syncRoutes.HasAnyEligibleAsync(candidateUserIds, targetDeviceId, ct);
    }

    private async Task RemoveCachedDeviceIfNoPendingAsync(UserDevice userDevice, CancellationToken ct)
    {
        if (userDevice.Device is null || await _syncQueueItems.HasPendingForDeviceAsync(userDevice.DeviceId, ct))
            return;

        if (await _syncRoutes.HasEligibleUserForDeviceAsync(userDevice.DeviceId, ct))
            return;

        _syncDeviceIdentities.TryRemove(userDevice.Device);
    }

    private string NormalizeUserDeviceName(string name)
    {
        if (!IsValidUserDeviceName(name)) throw new InvalidInputException();
        return name.Trim();
    }

    private async Task<UserDevice> GetActiveRemoteUserDeviceAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        if (deviceId == Guid.Empty || deviceId == _identity.LocalDeviceId) throw new InvalidInputException();
        var userDevice = await _userDevices.GetWithDeviceAsync(userId, deviceId, ct);
        if (userDevice is null || userDevice.IsDeleted || userDevice.Device is null) throw new InvalidInputException();
        return userDevice;
    }
}

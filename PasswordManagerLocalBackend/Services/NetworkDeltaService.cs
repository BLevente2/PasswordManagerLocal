using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Utils;
using static PasswordManagerLocalBackend.Constants.DataLengthConstants;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocalBackend.Utils.DataCodec;

namespace PasswordManagerLocalBackend.Services;

public sealed class NetworkDeltaService : INetworkDeltaService
{
    private readonly IOutgoingDeltaBuilderService _outgoingDeltaBuilder;
    private readonly IUserRepository _users;
    private readonly IGroupRepository _groups;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
    private readonly ILocalUserDeviceRepository _localUserDevices;
    private readonly ISyncTombstoneRepository _tombstones;
    private readonly ISyncQueueRepository _syncQueue;
    private readonly ISyncQueueService _syncQueueService;
    private readonly ISyncDeviceIdentityService _syncDeviceIdentities;
    private readonly IDeviceIdentityService _identity;
    private readonly ISyncAuthorizationService _authorization;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly IAuthService _auth;
    private readonly IKeyProtector _keyProtector;
    private readonly IUnitOfWork _uow;

    public NetworkDeltaService(
        IOutgoingDeltaBuilderService outgoingDeltaBuilder,
        IUserRepository users,
        IGroupRepository groups,
        IDeviceRepository devices,
        IUserDeviceRepository userDevices,
        ILocalUserDeviceRepository localUserDevices,
        ISyncTombstoneRepository tombstones,
        ISyncQueueRepository syncQueue,
        ISyncQueueService syncQueueService,
        ISyncDeviceIdentityService syncDeviceIdentities,
        IDeviceIdentityService identity,
        ISyncAuthorizationService authorization,
        ISyncRuntimeService syncRuntime,
        IAuthService auth,
        IKeyProtector keyProtector,
        IUnitOfWork uow)
    {
        _outgoingDeltaBuilder = outgoingDeltaBuilder;
        _users = users;
        _groups = groups;
        _devices = devices;
        _userDevices = userDevices;
        _localUserDevices = localUserDevices;
        _tombstones = tombstones;
        _syncQueue = syncQueue;
        _syncQueueService = syncQueueService;
        _syncDeviceIdentities = syncDeviceIdentities;
        _identity = identity;
        _authorization = authorization;
        _syncRuntime = syncRuntime;
        _auth = auth;
        _keyProtector = keyProtector;
        _uow = uow;
    }




    public Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default) =>
        _outgoingDeltaBuilder.BuildAsync(item, device, ct);


    public async Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn)
            throw new SyncRouteDisabledException("Local synchronization is disabled.");

        SyncCryptoUtil.ValidateEncryptedEnvelope(delta, _identity.LocalDeviceId);
        VerifyDeltaSignature(delta);
        var sourceDevice = await GetAndValidateSourceDeviceAsync(delta, ct);

        var plaintextPayload = DecryptPayload(delta);
        SyncDeltaPayload payload;
        try
        {
            payload = DeserializePayload(plaintextPayload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextPayload);
        }

        ValidateEnvelope(delta, payload);
        SyncCryptoUtil.ValidatePayloadIntegrity(payload, delta.Ts);
        await ValidateDeviceIdentityImmutabilityAsync(sourceDevice, payload, ct);

        if (await TryAcknowledgeAlreadyDeletedUserAsync(payload, sourceDevice, delta.Ts, ct))
        {
            await _uow.SaveChangesAsync(ct);
            return delta.Ts;
        }

        if (!await _authorization.CanReceiveAsync(payload, sourceDevice.Id, ct))
            throw new SyncRouteDisabledException("Synchronization is disabled for this user and device route.");
        await ValidateSourceCanApplyPayloadAsync(sourceDevice, payload, ct);

        if (await IsBlockedByNewerTombstoneAsync(payload, delta.Ts, ct))
        {
            await TouchSourceDeviceAsync(sourceDevice, ct);
            await _uow.SaveChangesAsync(ct);
            return delta.Ts;
        }

        if (await IsAlreadyAppliedAsync(payload, delta.Ts, ct))
        {
            await TouchSourceDeviceAsync(sourceDevice, ct);
            await _uow.SaveChangesAsync(ct);
            return delta.Ts;
        }

        var applied = payload.ModelType switch
        {
            SyncModelType.User => await ApplyUserAsync(payload, sourceDevice.Id, delta.Ts, ct),
            SyncModelType.Group => await ApplyGroupAsync(payload, delta.Ts, ct),
            SyncModelType.Device => await ApplyDeviceAsync(payload, delta.Ts, ct),
            SyncModelType.UserDevice => await ApplyUserDeviceAsync(payload, delta.Ts, ct),
            _ => throw new InvalidOperationException("Unknown sync model type.")
        };

        var deletesUserProfile = IsDeletedUserPayload(payload);

        if (!DeletesSourceDevice(payload, sourceDevice) && !DeletesLocalUserProfile(payload) && !deletesUserProfile)
            await TouchSourceDeviceAsync(sourceDevice, ct);

        if (applied && deletesUserProfile)
            await CleanupSourceDeviceAfterUserDeletionAsync(payload.ModelId, sourceDevice, ct);

        await _uow.SaveChangesAsync(ct);

        if (DeletesLocalUserProfile(payload) ||
            (payload.ModelType == SyncModelType.User && payload.ChangeType == SyncChangeType.Deleted))
        {
            await _syncRuntime.RefreshSyncEnabledAsync(ct);
        }

        if (applied)
            await RefreshAffectedSessionCachesAsync(payload, ct);

        if (applied && ShouldPropagate(payload))
            await PropagateIncomingDeltaAsync(payload, sourceDevice.Id, delta.Ts, ct);

        if (applied && IsRemoteUserDeviceDeletion(payload))
            await CleanupDetachedDeviceIfUserDeviceDeletionCompletedAsync(payload, delta.Ts, ct);

        if (applied &&
            payload.ModelType == SyncModelType.UserDevice &&
            payload.ChangeType != SyncChangeType.Deleted &&
            payload.UserDevice is { IsSyncOn: true, IsDeleted: false } enabledLink &&
            enabledLink.DeviceId != _identity.LocalDeviceId)
        {
            await _syncQueueService.EnqueueUserCatchUpAsync(enabledLink.UserId, enabledLink.DeviceId, ct);
        }

        return delta.Ts;
    }


    private async Task RefreshAffectedSessionCachesAsync(SyncDeltaPayload payload, CancellationToken ct)
    {
        if (payload.ModelType != SyncModelType.User)
            return;

        if (payload.ChangeType == SyncChangeType.Deleted)
        {
            _auth.LogoutUser(payload.ModelId, AuthSessionInvalidationReason.ProfileRemoved);
            return;
        }

        var user = await _users.GetByIdWithRelationsAsync(payload.ModelId, ct);
        if (user is null)
            return;

        await _auth.RefreshSyncedUserSessionsAsync(user, ct);
    }


    private async Task<bool> ApplyUserAsync(SyncDeltaPayload delta, Guid sourceDeviceId, long ts, CancellationToken ct)
    {
        var existing = await _users.GetByIdWithRelationsAsync(delta.ModelId, ct);

        if (delta.ChangeType == SyncChangeType.Deleted)
        {
            if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
                return false;

            if (existing is not null)
                await PropagateDeletedUserBeforeLocalRemovalAsync(existing, sourceDeviceId, ts, ct);

            if (existing is not null)
                _users.Delete(existing);

            await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);
            return true;
        }

        if (delta.User is null)
            throw new InvalidDataException("User sync payload is missing.");

        if (existing is not null && await TryApplyUserBlobMergeAsync(existing, delta.User, ts, ct))
        {
            await RemoveTombstoneAsync(delta, ct);
            return true;
        }

        if (existing is not null && !IncomingUserPayloadDominatesExistingBlobs(delta.User, existing))
            return false;

        if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
            return false;

        var user = existing ?? CreateUser(delta.User);
        CopyUserData(delta.User, user);
        user.LastModifiedAt = FromTimestamp(ts);

        if (existing is null)
            await _users.AddAsync(user, ct);

        await SyncUserGroupsAsync(user, delta.User.GroupIds, ct);
        await SyncUserDevicesAsync(user, delta.User.DeviceIds, user.LastModifiedAt, ct);
        user.GenerateIntegrityHash();
        await RemoveTombstoneAsync(delta, ct);
        return true;
    }


    private async Task PropagateDeletedUserBeforeLocalRemovalAsync(User user, Guid sourceDeviceId, long ts, CancellationToken ct)
    {
        await _syncQueueService.EnqueuePropagationAsync(new SyncItem
        {
            ModelId = user.UId,
            ModelType = SyncModelType.User,
            ChangeType = SyncChangeType.Deleted,
            ChangedAtTs = ts
        }, sourceDeviceId, ts, ct);
    }


    private async Task<bool> TryAcknowledgeAlreadyDeletedUserAsync(SyncDeltaPayload payload, Device sourceDevice, long ts, CancellationToken ct)
    {
        if (!IsDeletedUserPayload(payload))
            return false;

        var existing = await _users.GetByIdAsync(payload.ModelId, ct);
        if (existing is not null)
            return false;

        var tombstone = await _tombstones.GetAsync(payload.ModelId, SyncModelType.User, ct);
        if (tombstone is null)
            return false;

        if (ts > tombstone.DeletedAtTs)
            await _tombstones.UpsertAsync(payload.ModelId, SyncModelType.User, ts, ct);

        await CleanupSourceDeviceAfterUserDeletionAsync(payload.ModelId, sourceDevice, ct);
        return true;
    }


    private async Task CleanupSourceDeviceAfterUserDeletionAsync(Guid deletedUserId, Device sourceDevice, CancellationToken ct)
    {
        if (await _userDevices.HasAnyActiveLinkForDeviceExceptUserAsync(sourceDevice.Id, deletedUserId, ct))
        {
            await TouchSourceDeviceAsync(sourceDevice, ct);
            return;
        }

        _syncDeviceIdentities.TryRemove(sourceDevice);
        _devices.Delete(sourceDevice);
    }




    private static bool IncomingUserPayloadDominatesExistingBlobs(UserSyncPayload incoming, User existing) =>
        incoming.GeneralUserDataLastModifiedAt >= existing.GeneralUserDataLastModifiedAt &&
        incoming.UserPasswordsDataLastModifiedAt >= existing.UserPasswordsDataLastModifiedAt &&
        incoming.UserDevicesDataLastModifiedAt >= existing.UserDevicesDataLastModifiedAt;


    private async Task<bool> TryApplyUserBlobMergeAsync(User existing, UserSyncPayload incoming, long ts, CancellationToken ct)
    {
        var incomingTs = FromTimestamp(ts);

        if (!existing.PasswordSalt.SequenceEqual(incoming.PasswordSalt))
            return false;

        var incomingHasPotentiallyNewerEncryptedData =
            incoming.GeneralUserDataLastModifiedAt > existing.GeneralUserDataLastModifiedAt ||
            incoming.UserPasswordsDataLastModifiedAt > existing.UserPasswordsDataLastModifiedAt ||
            incoming.UserDevicesDataLastModifiedAt > existing.UserDevicesDataLastModifiedAt;
        var localHasPotentiallyNewerEncryptedData =
            existing.GeneralUserDataLastModifiedAt > incoming.GeneralUserDataLastModifiedAt ||
            existing.UserPasswordsDataLastModifiedAt > incoming.UserPasswordsDataLastModifiedAt ||
            existing.UserDevicesDataLastModifiedAt > incoming.UserDevicesDataLastModifiedAt;
        var relationUpdateIsNewer = incomingTs > existing.LastModifiedAt;
        var encryptedBlobsDiffer =
            !existing.EncryptedGeneralUserDataPayload.SequenceEqual(incoming.EncryptedGeneralUserDataPayload) ||
            !existing.EncryptedUserPasswordsDataPayload.SequenceEqual(incoming.EncryptedUserPasswordsDataPayload) ||
            !existing.EncryptedUserDevicesDataPayload.SequenceEqual(incoming.EncryptedUserDevicesDataPayload);

        if (!incomingHasPotentiallyNewerEncryptedData && !localHasPotentiallyNewerEncryptedData && !relationUpdateIsNewer && !encryptedBlobsDiffer)
            return false;

        if (!TryGetUserEncryptionKeyForSync(existing, out var userKey) || userKey is null)
            return false;

        try
        {
            var incomingUser = CreateUser(incoming);
            CopyUserData(incoming, incomingUser);
            incomingUser.LastModifiedAt = incomingTs;
            incomingUser.GenerateIntegrityHash();

            var existingBundle = await ReadAndVerifyUserDataBundleForSyncAsync(existing, userKey, ct);
            var incomingBundle = await ReadAndVerifyUserDataBundleForSyncAsync(incomingUser, userKey, ct);

            var changedBlobs = UserDataBlobKind.None;
            if (MergeGeneralUserDataForSync(existingBundle, incomingBundle, existing, incoming))
                changedBlobs |= UserDataBlobKind.General;

            if (MergeUserPasswordsDataForSync(existingBundle.UserPasswordsData, incomingBundle.UserPasswordsData))
                changedBlobs |= UserDataBlobKind.Passwords;

            if (MergeUserDevicesDataForSync(existingBundle.UserDevicesData, incomingBundle.UserDevicesData))
                changedBlobs |= UserDataBlobKind.Devices;

            if (changedBlobs != UserDataBlobKind.None)
            {
                await PersistMergedUserBundleAsync(existing, existingBundle, userKey, ct);

                existing.UserDataLastModifiedAt = MaxDateTimeOffset(existing.UserDataLastModifiedAt, incoming.UserDataLastModifiedAt, incomingTs);
                if (changedBlobs.HasFlag(UserDataBlobKind.General))
                    existing.GeneralUserDataLastModifiedAt = MaxDateTimeOffset(existing.GeneralUserDataLastModifiedAt, incoming.GeneralUserDataLastModifiedAt, incomingTs);
                if (changedBlobs.HasFlag(UserDataBlobKind.Passwords))
                    existing.UserPasswordsDataLastModifiedAt = MaxDateTimeOffset(existing.UserPasswordsDataLastModifiedAt, incoming.UserPasswordsDataLastModifiedAt, incomingTs);
                if (changedBlobs.HasFlag(UserDataBlobKind.Devices))
                    existing.UserDevicesDataLastModifiedAt = MaxDateTimeOffset(existing.UserDevicesDataLastModifiedAt, incoming.UserDevicesDataLastModifiedAt, incomingTs);
            }

            if (relationUpdateIsNewer)
            {
                await SyncUserGroupsAsync(existing, incoming.GroupIds, ct);
                await SyncUserDevicesAsync(existing, incoming.DeviceIds, incomingTs, ct);
            }

            existing.LastModifiedAt = MaxDateTimeOffset(existing.LastModifiedAt, incomingTs);
            existing.GenerateIntegrityHash();
            _users.Update(existing);
            return changedBlobs != UserDataBlobKind.None || relationUpdateIsNewer;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            userKey.Dispose();
        }
    }


    private bool TryGetUserEncryptionKeyForSync(User user, out EncryptionKey? key)
    {
        if (_auth.TryGetActiveUserEncryptionKey(user.UId, out key) && key is not null)
            return true;

        key = null;
        if (user.SavedKey is null || user.SavedKey.Length == 0)
            return false;

        byte[]? rawKey = null;
        try
        {
            rawKey = _keyProtector.Unprotect(user.SavedKey);
            key = EncryptionKey.FromRaw(rawKey);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            key = null;
            return false;
        }
        finally
        {
            if (rawKey is not null)
                CryptographicOperations.ZeroMemory(rawKey);
        }
    }


    private static bool MergeGeneralUserDataForSync(UserDataBundle local, UserDataBundle incoming, User existingUser, UserSyncPayload incomingUser)
    {
        var localUpdatedAt = local.GeneralUserData.LastUpdatedAt;
        var incomingUpdatedAt = incoming.GeneralUserData.LastUpdatedAt;
        if (incomingUpdatedAt <= localUpdatedAt)
            return false;

        local.GeneralUserData.Username = incoming.GeneralUserData.Username;
        local.GeneralUserData.FirstName = incoming.GeneralUserData.FirstName;
        local.GeneralUserData.LastName = incoming.GeneralUserData.LastName;
        local.GeneralUserData.Email = incoming.GeneralUserData.Email;
        local.GeneralUserData.RegistrationDate = incoming.GeneralUserData.RegistrationDate;
        local.GeneralUserData.LastUpdatedAt = incoming.GeneralUserData.LastUpdatedAt;

        CryptographicOperations.ZeroMemory(existingUser.UsernameHash);
        CryptographicOperations.ZeroMemory(existingUser.UsernameSalt);
        existingUser.UsernameHash = incomingUser.UsernameHash.ToArray();
        existingUser.UsernameSalt = incomingUser.UsernameSalt.ToArray();
        return true;
    }


    private static bool MergeUserPasswordsDataForSync(UserPasswordsData local, UserPasswordsData incoming)
    {
        if (!local.PasswordKey.SequenceEqual(incoming.PasswordKey))
            return false;

        var passwordEntriesChanged = MergePasswordEntriesForSync(local, incoming);
        var customColorsChanged = MergeCustomColorsForSync(local, incoming);
        return passwordEntriesChanged || customColorsChanged;
    }


    private static bool MergePasswordEntriesForSync(UserPasswordsData local, UserPasswordsData incoming)
    {
        var changed = false;
        var localPasswords = local.Passwords.ToDictionary(password => password.Id);
        var incomingPasswords = incoming.Passwords.ToDictionary(password => password.Id);
        var localDeleted = local.DeletedPasswords.ToDictionary(deleted => deleted.Id);
        var incomingDeleted = incoming.DeletedPasswords.ToDictionary(deleted => deleted.Id);
        var ids = localPasswords.Keys
            .Concat(incomingPasswords.Keys)
            .Concat(localDeleted.Keys)
            .Concat(incomingDeleted.Keys)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var mergedPasswords = new List<SecurePassword>();
        var mergedDeleted = new List<DeletedPasswordData>();
        foreach (var id in ids)
        {
            localPasswords.TryGetValue(id, out var localPassword);
            incomingPasswords.TryGetValue(id, out var incomingPassword);
            localDeleted.TryGetValue(id, out var localDeletion);
            incomingDeleted.TryGetValue(id, out var incomingDeletion);

            var newestPassword = NewerPassword(localPassword, incomingPassword);
            var newestDeletion = NewerDeletedPassword(localDeletion, incomingDeletion);
            var passwordTime = newestPassword?.LastUpdatedAt ?? DateTime.MinValue;
            var deletionTime = newestDeletion?.DeletedAt ?? DateTime.MinValue;

            if (newestDeletion is not null && deletionTime >= passwordTime)
            {
                mergedDeleted.Add(newestDeletion);
                if (localPassword is not null || !ReferenceEquals(localDeletion, newestDeletion))
                    changed = true;
                continue;
            }

            if (newestPassword is not null)
            {
                mergedPasswords.Add(newestPassword);
                if (!ReferenceEquals(localPassword, newestPassword) || localDeletion is not null)
                    changed = true;
            }
        }

        changed |= local.Passwords.Count != mergedPasswords.Count || local.DeletedPasswords.Count != mergedDeleted.Count;
        if (!changed)
            return TombstoneCleanupUtil.EnforceDeletedPasswordTombstoneLimit(local.DeletedPasswords);

        DisposeItemsNotKept(local.Passwords, mergedPasswords);
        DisposeItemsNotKept(local.DeletedPasswords, mergedDeleted);
        local.Passwords = mergedPasswords.OrderBy(password => password.Name, StringComparer.OrdinalIgnoreCase).ThenBy(password => password.Id).ToList();
        local.DeletedPasswords = mergedDeleted.OrderBy(deleted => deleted.DeletedAt).ThenBy(deleted => deleted.Id).ToList();
        TombstoneCleanupUtil.EnforceDeletedPasswordTombstoneLimit(local.DeletedPasswords);
        return true;
    }


    private static bool MergeCustomColorsForSync(UserPasswordsData local, UserPasswordsData incoming)
    {
        var changed = false;
        var localColors = local.CustomColors.ToDictionary(color => color.Id);
        var incomingColors = incoming.CustomColors.ToDictionary(color => color.Id);
        var localDeleted = local.DeletedCustomColors.ToDictionary(deleted => deleted.Id);
        var incomingDeleted = incoming.DeletedCustomColors.ToDictionary(deleted => deleted.Id);
        var ids = localColors.Keys
            .Concat(incomingColors.Keys)
            .Concat(localDeleted.Keys)
            .Concat(incomingDeleted.Keys)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var mergedColors = new List<CustomUserColor>();
        var mergedDeleted = new List<DeletedCustomUserColorData>();
        foreach (var id in ids)
        {
            localColors.TryGetValue(id, out var localColor);
            incomingColors.TryGetValue(id, out var incomingColor);
            localDeleted.TryGetValue(id, out var localDeletion);
            incomingDeleted.TryGetValue(id, out var incomingDeletion);

            var newestColor = NewerCustomColor(localColor, incomingColor);
            var newestDeletion = NewerDeletedCustomColor(localDeletion, incomingDeletion);
            var colorTime = newestColor?.LastUpdatedAt ?? DateTime.MinValue;
            var deletionTime = newestDeletion?.DeletedAt ?? DateTime.MinValue;

            if (newestDeletion is not null && deletionTime >= colorTime)
            {
                mergedDeleted.Add(newestDeletion);
                if (localColor is not null || !ReferenceEquals(localDeletion, newestDeletion))
                    changed = true;
                continue;
            }

            if (newestColor is not null)
            {
                newestColor.ColorCode = NormalizeCustomColorCodeForSync(newestColor.ColorCode);
                newestColor.ColorName = NormalizeCustomColorNameForSync(newestColor.ColorName);
                mergedColors.Add(newestColor);
                if (!ReferenceEquals(localColor, newestColor) || localDeletion is not null)
                    changed = true;
            }
        }

        var deduplicatedColors = ResolveDuplicateCustomColorCodesForSync(mergedColors);
        changed |= deduplicatedColors.Count != mergedColors.Count;
        changed |= local.CustomColors.Count != deduplicatedColors.Count || local.DeletedCustomColors.Count != mergedDeleted.Count;
        if (!changed)
            return TombstoneCleanupUtil.EnforceDeletedCustomUserColorTombstoneLimit(local.DeletedCustomColors);

        DisposeItemsNotKept(local.CustomColors, deduplicatedColors);
        DisposeItemsNotKept(local.DeletedCustomColors, mergedDeleted);
        local.CustomColors = deduplicatedColors
            .OrderBy(color => color.ColorName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(color => color.ColorCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(color => color.Id)
            .ToList();
        local.DeletedCustomColors = mergedDeleted.OrderBy(deleted => deleted.DeletedAt).ThenBy(deleted => deleted.Id).ToList();
        TombstoneCleanupUtil.EnforceDeletedCustomUserColorTombstoneLimit(local.DeletedCustomColors);
        return true;
    }


    private static CustomUserColor? NewerCustomColor(CustomUserColor? first, CustomUserColor? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.LastUpdatedAt > first.LastUpdatedAt)
            return second;
        return first;
    }


    private static DeletedCustomUserColorData? NewerDeletedCustomColor(DeletedCustomUserColorData? first, DeletedCustomUserColorData? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.DeletedAt > first.DeletedAt)
            return second;
        return first;
    }


    private static List<CustomUserColor> ResolveDuplicateCustomColorCodesForSync(List<CustomUserColor> colors)
    {
        var keptByCode = new Dictionary<string, CustomUserColor>(StringComparer.OrdinalIgnoreCase);
        foreach (var color in colors.OrderByDescending(color => color.LastUpdatedAt).ThenBy(color => color.Id))
        {
            var normalizedCode = NormalizeCustomColorCodeForSync(color.ColorCode);
            if (normalizedCode.Length == 0)
                continue;

            color.ColorCode = normalizedCode;
            color.ColorName = NormalizeCustomColorNameForSync(color.ColorName);

            if (!keptByCode.ContainsKey(normalizedCode))
                keptByCode[normalizedCode] = color;
        }

        var kept = keptByCode.Values.ToList();
        foreach (var color in colors)
        {
            if (!kept.Any(keptColor => ReferenceEquals(keptColor, color)))
                color.Dispose();
        }

        return kept;
    }


    private static string NormalizeCustomColorCodeForSync(string colorCode)
    {
        var normalized = colorCode.Trim().ToUpperInvariant();
        if (normalized.Length != ARGBColorLength || normalized[0] != '#')
            return string.Empty;

        return uint.TryParse(normalized.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _)
            ? normalized
            : string.Empty;
    }


    private static string? NormalizeCustomColorNameForSync(string? colorName)
    {
        if (colorName is null)
            return null;

        var trimmed = colorName.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }



    private static void DisposeItemsNotKept<T>(IEnumerable<T> currentItems, IReadOnlyCollection<T> keptItems) where T : class, IDisposable
    {
        foreach (var current in currentItems)
        {
            if (!keptItems.Any(kept => ReferenceEquals(kept, current)))
                current.Dispose();
        }
    }


    private static SecurePassword? NewerPassword(SecurePassword? first, SecurePassword? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.LastUpdatedAt > first.LastUpdatedAt)
            return second;
        return first;
    }


    private static DeletedPasswordData? NewerDeletedPassword(DeletedPasswordData? first, DeletedPasswordData? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.DeletedAt > first.DeletedAt)
            return second;
        return first;
    }


    private static bool MergeUserDevicesDataForSync(UserDevicesData local, UserDevicesData incoming)
    {
        var changed = false;
        var localDevices = local.Devices.ToDictionary(device => device.Id);
        var incomingDevices = incoming.Devices.ToDictionary(device => device.Id);
        var localDeleted = local.DeletedDevices.ToDictionary(deleted => deleted.Id);
        var incomingDeleted = incoming.DeletedDevices.ToDictionary(deleted => deleted.Id);
        var ids = localDevices.Keys
            .Concat(incomingDevices.Keys)
            .Concat(localDeleted.Keys)
            .Concat(incomingDeleted.Keys)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var mergedDevices = new List<UserDeviceData>();
        var mergedDeleted = new List<DeletedUserDeviceData>();
        foreach (var id in ids)
        {
            localDevices.TryGetValue(id, out var localDevice);
            incomingDevices.TryGetValue(id, out var incomingDevice);
            localDeleted.TryGetValue(id, out var localDeletion);
            incomingDeleted.TryGetValue(id, out var incomingDeletion);

            var newestDevice = NewerDevice(localDevice, incomingDevice);
            var newestDeletion = NewerDeletedDevice(localDeletion, incomingDeletion);
            var deviceTime = newestDevice?.LastUpdatedAt ?? DateTimeOffset.MinValue;
            var deletionTime = newestDeletion?.DeletedAt ?? DateTimeOffset.MinValue;

            if (newestDeletion is not null && deletionTime >= deviceTime)
            {
                mergedDeleted.Add(newestDeletion);
                if (localDevice is not null || !ReferenceEquals(localDeletion, newestDeletion))
                    changed = true;
                continue;
            }

            if (newestDevice is not null)
            {
                if (localDevice is not null && incomingDevice is not null)
                {
                    var newestLogin = localDevice.LastLoginDate >= incomingDevice.LastLoginDate
                        ? localDevice.LastLoginDate
                        : incomingDevice.LastLoginDate;
                    if (newestDevice.LastLoginDate != newestLogin)
                    {
                        newestDevice.LastLoginDate = newestLogin;
                        changed = true;
                    }
                }

                mergedDevices.Add(newestDevice);
                if (!ReferenceEquals(localDevice, newestDevice) || localDeletion is not null)
                    changed = true;
            }
        }

        changed |= local.Devices.Count != mergedDevices.Count || local.DeletedDevices.Count != mergedDeleted.Count;
        if (!changed)
            return TombstoneCleanupUtil.EnforceDeletedUserDeviceTombstoneLimit(local.DeletedDevices);

        DisposeItemsNotKept(local.Devices, mergedDevices);
        DisposeItemsNotKept(local.DeletedDevices, mergedDeleted);
        local.Devices = ResolveDuplicateDeviceNamesForSync(mergedDevices);
        local.DeletedDevices = mergedDeleted.OrderBy(deleted => deleted.DeletedAt).ThenBy(deleted => deleted.Id).ToList();
        TombstoneCleanupUtil.EnforceDeletedUserDeviceTombstoneLimit(local.DeletedDevices);
        return true;
    }


    private static UserDeviceData? NewerDevice(UserDeviceData? first, UserDeviceData? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.LastUpdatedAt > first.LastUpdatedAt)
            return second;
        return first;
    }


    private static DeletedUserDeviceData? NewerDeletedDevice(DeletedUserDeviceData? first, DeletedUserDeviceData? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.DeletedAt > first.DeletedAt)
            return second;
        return first;
    }


    private static List<UserDeviceData> ResolveDuplicateDeviceNamesForSync(List<UserDeviceData> devices)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices.OrderByDescending(device => device.LastUpdatedAt).ThenBy(device => device.Id))
        {
            var baseName = string.IsNullOrWhiteSpace(device.Name) ? DeviceNameUtil.BuildDefaultDeviceName(device.Id) : device.Name.Trim();
            device.Name = BuildUniqueDeviceNameForSync(baseName, usedNames, device.Id);
            usedNames.Add(device.Name);
        }

        return devices.OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase).ThenBy(device => device.Id).ToList();
    }


    private static string BuildUniqueDeviceNameForSync(string requestedName, HashSet<string> usedNames, Guid deviceId)
    {
        var baseName = requestedName.Trim();
        if (baseName.Length == 0)
            baseName = DeviceNameUtil.BuildDefaultDeviceName(deviceId);

        if (baseName.Length > UserDeviceNameMaxLength)
            baseName = baseName[..UserDeviceNameMaxLength];

        if (!usedNames.Contains(baseName))
            return baseName;

        var suffixSeed = deviceId.ToString("N")[..6];
        for (var i = 2; i < 100; i++)
        {
            var suffix = $"-{suffixSeed}-{i}";
            var prefixLength = Math.Max(1, UserDeviceNameMaxLength - suffix.Length);
            var candidate = baseName[..Math.Min(baseName.Length, prefixLength)] + suffix;
            if (!usedNames.Contains(candidate))
                return candidate;
        }

        return deviceId.ToString("N")[..UserDeviceNameMaxLength];
    }


    private async Task<UserDataBundle> ReadAndVerifyUserDataBundleForSyncAsync(User user, EncryptionKey userKey, CancellationToken ct)
    {
        var userData = await DecryptDecompressDeserializeAsync(user.EncryptedPayload, userKey, BackendJsonSerializerContext.Default.UserData, ct: ct);
        if (userData is null)
            throw new UnauthorizedAccessException();

        userData.VerifyIntegrity();

        var general = await DecryptEncryptedUserBlobAsync(user.EncryptedGeneralUserDataPayload, userData.GeneralUserDataKey, BackendJsonSerializerContext.Default.GeneralUserData, ct);
        var passwords = await DecryptEncryptedUserBlobAsync(user.EncryptedUserPasswordsDataPayload, userData.UserPasswordsDataKey, BackendJsonSerializerContext.Default.UserPasswordsData, ct);
        var devices = await DecryptEncryptedUserBlobAsync(user.EncryptedUserDevicesDataPayload, userData.UserDevicesDataKey, BackendJsonSerializerContext.Default.UserDevicesData, ct);

        var bundle = new UserDataBundle
        {
            UserData = userData,
            GeneralUserData = general,
            UserPasswordsData = passwords,
            UserDevicesData = devices
        };

        VerifyUserDataBundleForSync(bundle);
        return bundle;
    }


    private static async Task<T> DecryptEncryptedUserBlobAsync<T>(byte[] encryptedBlob, byte[] rawKey, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken ct) where T : class
    {
        if (encryptedBlob.Length == 0 || rawKey.Length == 0)
            throw new UnauthorizedAccessException();

        using var key = EncryptionKey.FromRaw(rawKey);
        var data = await DecryptDecompressDeserializeAsync(encryptedBlob, key, typeInfo, ct: ct);
        if (data is null)
            throw new UnauthorizedAccessException();

        return data;
    }


    private async Task PersistMergedUserBundleAsync(User user, UserDataBundle bundle, EncryptionKey userKey, CancellationToken ct)
    {
        GenerateAndCopyUserBundleHashesForSync(bundle);
        VerifyUserDataBundleForSync(bundle);

        using (var generalKey = EncryptionKey.FromRaw(bundle.UserData.GeneralUserDataKey))
        {
            var encrypted = await SerializeCompressEncryptAsync(bundle.GeneralUserData, generalKey, BackendJsonSerializerContext.Default.GeneralUserData, ct: ct);
            CryptographicOperations.ZeroMemory(user.EncryptedGeneralUserDataPayload);
            user.EncryptedGeneralUserDataPayload = encrypted;
        }

        using (var passwordsKey = EncryptionKey.FromRaw(bundle.UserData.UserPasswordsDataKey))
        {
            var encrypted = await SerializeCompressEncryptAsync(bundle.UserPasswordsData, passwordsKey, BackendJsonSerializerContext.Default.UserPasswordsData, ct: ct);
            CryptographicOperations.ZeroMemory(user.EncryptedUserPasswordsDataPayload);
            user.EncryptedUserPasswordsDataPayload = encrypted;
        }

        using (var devicesKey = EncryptionKey.FromRaw(bundle.UserData.UserDevicesDataKey))
        {
            var encrypted = await SerializeCompressEncryptAsync(bundle.UserDevicesData, devicesKey, BackendJsonSerializerContext.Default.UserDevicesData, ct: ct);
            CryptographicOperations.ZeroMemory(user.EncryptedUserDevicesDataPayload);
            user.EncryptedUserDevicesDataPayload = encrypted;
        }

        var encryptedUserData = await SerializeCompressEncryptAsync(bundle.UserData, userKey, BackendJsonSerializerContext.Default.UserData, ct: ct);
        CryptographicOperations.ZeroMemory(user.EncryptedPayload);
        user.EncryptedPayload = encryptedUserData;
    }


    private static void GenerateAndCopyUserBundleHashesForSync(UserDataBundle bundle)
    {
        bundle.GeneralUserData.GenerateIntegrityHash();

        foreach (var password in bundle.UserPasswordsData.Passwords)
            password.GenerateIntegrityHash();
        foreach (var deleted in bundle.UserPasswordsData.DeletedPasswords)
            deleted.GenerateIntegrityHash();
        foreach (var color in bundle.UserPasswordsData.CustomColors)
            color.GenerateIntegrityHash();
        foreach (var deleted in bundle.UserPasswordsData.DeletedCustomColors)
            deleted.GenerateIntegrityHash();
        bundle.UserPasswordsData.GenerateIntegrityHash();

        foreach (var device in bundle.UserDevicesData.Devices)
            device.GenerateIntegrityHash();
        foreach (var deleted in bundle.UserDevicesData.DeletedDevices)
            deleted.GenerateIntegrityHash();
        bundle.UserDevicesData.GenerateIntegrityHash();

        CryptographicOperations.ZeroMemory(bundle.UserData.GeneralUserDataIntegrityHash);
        CryptographicOperations.ZeroMemory(bundle.UserData.UserPasswordsDataIntegrityHash);
        CryptographicOperations.ZeroMemory(bundle.UserData.UserDevicesDataIntegrityHash);
        bundle.UserData.GeneralUserDataIntegrityHash = bundle.GeneralUserData.IntegrityHash.ToArray();
        bundle.UserData.UserPasswordsDataIntegrityHash = bundle.UserPasswordsData.IntegrityHash.ToArray();
        bundle.UserData.UserDevicesDataIntegrityHash = bundle.UserDevicesData.IntegrityHash.ToArray();
        bundle.UserData.GenerateIntegrityHash();
    }


    private static void VerifyUserDataBundleForSync(UserDataBundle bundle)
    {
        bundle.UserData.VerifyIntegrity();
        bundle.GeneralUserData.VerifyIntegrity();
        VerifyStoredUserBlobHashForSync(bundle.UserData.GeneralUserDataIntegrityHash, bundle.GeneralUserData.IntegrityHash, typeof(GeneralUserData));

        foreach (var password in bundle.UserPasswordsData.Passwords)
            password.VerifyIntegrity();
        foreach (var deleted in bundle.UserPasswordsData.DeletedPasswords)
            deleted.VerifyIntegrity();
        foreach (var color in bundle.UserPasswordsData.CustomColors)
            color.VerifyIntegrity();
        foreach (var deleted in bundle.UserPasswordsData.DeletedCustomColors)
            deleted.VerifyIntegrity();
        bundle.UserPasswordsData.VerifyIntegrity();
        VerifyStoredUserBlobHashForSync(bundle.UserData.UserPasswordsDataIntegrityHash, bundle.UserPasswordsData.IntegrityHash, typeof(UserPasswordsData));

        foreach (var device in bundle.UserDevicesData.Devices)
            device.VerifyIntegrity();
        foreach (var deleted in bundle.UserDevicesData.DeletedDevices)
            deleted.VerifyIntegrity();
        bundle.UserDevicesData.VerifyIntegrity();
        VerifyStoredUserBlobHashForSync(bundle.UserData.UserDevicesDataIntegrityHash, bundle.UserDevicesData.IntegrityHash, typeof(UserDevicesData));
    }


    private static void VerifyStoredUserBlobHashForSync(byte[] expected, byte[] actual, Type type)
    {
        if (expected.Length != Hashing.SHA256HashSizeInBytes ||
            actual.Length != Hashing.SHA256HashSizeInBytes ||
            !Hashing.Verify(expected, actual))
            throw new InvalidDataIntegrityException(type);
    }


    private static DateTimeOffset MaxDateTimeOffset(params DateTimeOffset[] values)
    {
        var max = DateTimeOffset.MinValue;
        foreach (var value in values)
        {
            if (value != default && value > max)
                max = value;
        }

        return max == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : max;
    }


    private async Task<bool> ApplyGroupAsync(SyncDeltaPayload delta, long ts, CancellationToken ct)
    {
        var existing = await _groups.GetByIdWithUsersAsync(delta.ModelId, ct);

        if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
            return false;

        if (delta.ChangeType == SyncChangeType.Deleted)
        {
            if (existing is not null)
                _groups.Delete(existing);

            await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);
            return true;
        }

        if (delta.Group is null)
            throw new InvalidDataException("Group sync payload is missing.");

        var group = existing ?? CreateGroup(delta.Group);
        CopyGroupData(delta.Group, group);
        group.LastModifiedAt = FromTimestamp(ts);

        if (existing is null)
            await _groups.AddAsync(group, ct);

        await SyncGroupUsersAsync(group, delta.Group.UserIds, ct);
        group.GenerateIntegrityHash();
        await RemoveTombstoneAsync(delta, ct);
        return true;
    }


    private async Task<bool> ApplyDeviceAsync(SyncDeltaPayload delta, long ts, CancellationToken ct)
    {
        if (IsLocalDevicePayload(delta))
            return false;

        var existing = await _devices.GetByIdWithUsersAsync(delta.ModelId, ct);

        if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
            return false;

        if (delta.ChangeType == SyncChangeType.Deleted)
        {
            if (existing is not null)
            {
                _syncDeviceIdentities.TryRemove(existing);
                _devices.Delete(existing);
            }

            await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);
            return true;
        }

        if (delta.Device is null)
            throw new InvalidDataException("Device sync payload is missing.");

        var isNew = existing is null;
        var device = existing ?? CreateDevice(delta.Device);
        CopyDeviceData(delta.Device, device, isNew);
        device.LastModifiedAt = FromTimestamp(ts);

        if (isNew)
            await _devices.AddAsync(device, ct);

        await SyncDeviceUsersAsync(device, delta.Device.UserIds, device.LastModifiedAt, ct);
        device.GenerateIntegrityHash();
        await RemoveTombstoneAsync(delta, ct);

        await RefreshCachedDeviceAsync(device, ct);
        return true;
    }


    private async Task<bool> ApplyUserDeviceAsync(SyncDeltaPayload delta, long ts, CancellationToken ct)
    {
        if (delta.UserDevice is null)
            throw new InvalidDataException("User device sync payload is missing.");

        ValidateUserDevicePayload(delta);

        if (DeletesLocalUserProfile(delta))
            return await ApplyLocalUserProfileDisconnectAsync(delta, ts, ct);

        var payload = delta.UserDevice;
        var existing = await _userDevices.GetAsync(payload.UserId, payload.DeviceId, ct);
        existing?.VerifyIntegrity();
        if (existing is not null && IsIncomingOlderOrSame(existing.LastModifiedAt, ts))
            return false;

        var modifiedAt = FromTimestamp(ts);
        if (delta.ChangeType == SyncChangeType.Deleted || payload.IsDeleted)
        {
            if (existing is not null)
            {
                existing.IsDeleted = true;
                existing.IsSyncOn = false;
                existing.DeletedAt = payload.DeletedAt ?? modifiedAt;
                existing.LastModifiedAt = modifiedAt;
                existing.GenerateIntegrityHash();
                _userDevices.Update(existing);
            }

            await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);
            if (payload.DeviceId != _identity.LocalDeviceId)
                await RemovePendingSyncsForUserToDeviceAsync(payload.UserId, payload.DeviceId, ct);
            return true;
        }

        var remoteDevice = await _devices.GetByIdAsync(payload.DeviceId, ct);
        if (remoteDevice is null)
            throw new InvalidDataException("Remote device was not found for the user-device setting.");

        var userDevice = existing ?? new UserDevice
        {
            UserId = payload.UserId,
            DeviceId = payload.DeviceId
        };

        userDevice.Device = remoteDevice;
        userDevice.IsDeleted = false;
        userDevice.IsSyncOn = payload.IsSyncOn;
        userDevice.DeletedAt = null;
        userDevice.LastModifiedAt = modifiedAt;
        userDevice.GenerateIntegrityHash();

        if (existing is null)
            await _userDevices.AddAsync(userDevice, ct);
        else
            _userDevices.Update(userDevice);

        await RemoveTombstoneAsync(delta, ct);
        return true;
    }


    private async Task CleanupDetachedDeviceIfUserDeviceDeletionCompletedAsync(SyncDeltaPayload payload, long ts, CancellationToken ct)
    {
        if (payload.UserDevice is null || payload.UserDevice.DeviceId == _identity.LocalDeviceId)
            return;

        if (await _syncQueue.HasPendingForModelAsync(payload.ModelId, SyncModelType.UserDevice, ct))
            return;

        var deletedUserDevice = await _userDevices.GetByModelIdAsync(payload.ModelId, ct);
        if (deletedUserDevice is not null && !deletedUserDevice.IsDeleted)
            return;

        await _tombstones.UpsertAsync(payload.ModelId, SyncModelType.UserDevice, ts, ct);

        if (!await _userDevices.HasAnyActiveLinkForDeviceAsync(payload.UserDevice.DeviceId, ct))
        {
            var device = await _devices.GetByIdWithUserDevicesAsync(payload.UserDevice.DeviceId, ct);
            if (device is not null)
            {
                _syncDeviceIdentities.TryRemove(device);
                _devices.Delete(device);
            }
        }
        else if (deletedUserDevice is not null)
        {
            _userDevices.Delete(deletedUserDevice);
        }

        await _uow.SaveChangesAsync(ct);
    }


    private async Task RemovePendingSyncsForUserToDeviceAsync(Guid userId, Guid targetDeviceId, CancellationToken ct)
    {
        var pendingItems = await _syncQueue.ListPendingForDeviceWithItemsAsync(targetDeviceId, ct);
        foreach (var queueItem in pendingItems)
        {
            if (queueItem.SyncItem is not null && await IsSyncItemOnlyForRemovedUserOrRouteAsync(queueItem.SyncItem, userId, targetDeviceId, ct))
                _syncQueue.Delete(queueItem);
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
            var group = await _groups.GetByIdWithUsersAsync(item.ModelId, ct);
            if (group is null || group.Users.All(user => user.UId != removedUserId))
                return false;

            return !await AnyOtherUserCanStillSyncToTargetAsync(
                group.Users.Select(user => user.UId),
                removedUserId,
                targetDeviceId,
                ct);
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

    private async Task<bool> AnyOtherUserCanStillSyncToTargetAsync(IEnumerable<Guid> userIds, Guid removedUserId, Guid targetDeviceId, CancellationToken ct)
    {
        foreach (var otherUserId in userIds.Where(id => id != Guid.Empty && id != removedUserId).Distinct())
        {
            if (await _localUserDevices.IsSyncOnAsync(otherUserId, ct) &&
                await _userDevices.HasActiveLinkAsync(otherUserId, targetDeviceId, ct))
                return true;
        }

        return false;
    }


    private async Task<bool> ApplyLocalUserProfileDisconnectAsync(SyncDeltaPayload delta, long ts, CancellationToken ct)
    {
        if (delta.UserDevice is null)
            throw new InvalidDataException("User device sync payload is missing.");

        await _tombstones.UpsertAsync(delta.ModelId, delta.ModelType, ts, ct);

        var user = await _users.GetByIdWithRelationsAsync(delta.UserDevice.UserId, ct);
        if (user is null)
            return false;

        var relatedDeviceIds = user.UserDevices
            .Where(ud => ud.DeviceId != Guid.Empty && ud.DeviceId != _identity.LocalDeviceId)
            .Select(ud => ud.DeviceId)
            .Distinct()
            .ToList();

        foreach (var deviceId in relatedDeviceIds)
            await RemovePendingSyncsForUserToDeviceAsync(user.UId, deviceId, ct);

        _auth.LogoutUser(user.UId, AuthSessionInvalidationReason.ProfileRemoved);
        _users.Delete(user);

        foreach (var deviceId in relatedDeviceIds)
        {
            if (await _userDevices.HasAnyActiveLinkForDeviceExceptUserAsync(deviceId, user.UId, ct))
                continue;

            var device = await _devices.GetByIdWithUserDevicesAsync(deviceId, ct);
            if (device is null)
                continue;

            _syncDeviceIdentities.TryRemove(device);
            _devices.Delete(device);
        }

        return true;
    }


    private async Task SyncUserGroupsAsync(User user, IEnumerable<Guid> groupIds, CancellationToken ct)
    {
        var ids = CreateIdSet(groupIds);

        foreach (var group in user.Groups.Where(g => !ids.Contains(g.Id)).ToList())
            user.Groups.Remove(group);

        var missingIds = ids.Where(id => user.Groups.All(g => g.Id != id)).ToArray();
        var groups = await _groups.ListByIdsAsync(missingIds, ct);
        foreach (var group in groups)
            user.Groups.Add(group);
    }


    private async Task SyncUserDevicesAsync(User user, IEnumerable<Guid> deviceIds, DateTimeOffset modifiedAt, CancellationToken ct)
    {
        var ids = CreateIdSet(deviceIds);
        ids.Remove(_identity.LocalDeviceId);

        var devices = (await _devices.ListByIdsAsync(ids, ct)).ToDictionary(device => device.Id);
        var knownLinks = user.UserDevices.ToDictionary(link => link.DeviceId);
        foreach (var id in ids)
        {
            if (!devices.TryGetValue(id, out var remoteDevice))
                continue;

            if (knownLinks.TryGetValue(id, out var existingLink))
            {
                existingLink.Device = remoteDevice;
                existingLink.VerifyIntegrity();
                if (existingLink.IsDeleted)
                {
                    existingLink.IsDeleted = false;
                    existingLink.DeletedAt = null;
                    existingLink.IsSyncOn = false;
                    existingLink.LastModifiedAt = modifiedAt;
                    existingLink.GenerateIntegrityHash();
                }
                _userDevices.Update(existingLink);
                continue;
            }

            var newLink = new UserDevice
            {
                UserId = user.UId,
                DeviceId = id,
                User = user,
                Device = remoteDevice,
                IsSyncOn = false,
                IsDeleted = false,
                LastModifiedAt = modifiedAt
            };
            newLink.GenerateIntegrityHash();
            user.UserDevices.Add(newLink);
        }
    }


    private async Task SyncGroupUsersAsync(Group group, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = CreateIdSet(userIds);

        foreach (var user in group.Users.Where(u => !ids.Contains(u.UId)).ToList())
            group.Users.Remove(user);

        var missingIds = ids.Where(id => group.Users.All(u => u.UId != id)).ToArray();
        var users = await _users.ListByIdsAsync(missingIds, ct);
        foreach (var user in users)
            group.Users.Add(user);
    }


    private async Task SyncDeviceUsersAsync(Device device, IEnumerable<Guid> userIds, DateTimeOffset modifiedAt, CancellationToken ct)
    {
        var ids = CreateIdSet(userIds);

        var existingLinks = (await _userDevices.ListByUserIdsAndDeviceAsync(ids, device.Id, ct))
            .ToDictionary(link => link.UserId);
        var missingUserIds = ids.Where(id => !existingLinks.ContainsKey(id)).ToArray();
        var users = (await _users.ListByIdsAsync(missingUserIds, ct)).ToDictionary(user => user.UId);

        foreach (var id in ids)
        {
            if (existingLinks.TryGetValue(id, out var existingLink))
            {
                existingLink.Device = device;
                existingLink.VerifyIntegrity();
                if (existingLink.IsDeleted)
                {
                    existingLink.IsDeleted = false;
                    existingLink.DeletedAt = null;
                    existingLink.IsSyncOn = false;
                    existingLink.LastModifiedAt = modifiedAt;
                    existingLink.GenerateIntegrityHash();
                }
                _userDevices.Update(existingLink);
                continue;
            }

            if (!users.TryGetValue(id, out var user))
                continue;

            var newLink = new UserDevice
            {
                UserId = user.UId,
                DeviceId = device.Id,
                User = user,
                Device = device,
                IsSyncOn = false,
                IsDeleted = false,
                LastModifiedAt = modifiedAt
            };
            newLink.GenerateIntegrityHash();
            await _userDevices.AddAsync(newLink, ct);
        }
    }


    private async Task<Device> GetAndValidateSourceDeviceAsync(NetworkDelta delta, CancellationToken ct)
    {
        var sourceDevice = await _devices.GetBySignPublicKeyAsync(delta.SignPub, ct);
        if (sourceDevice is null)
            throw new UnauthorizedAccessException("Unknown sync source device.");

        if (sourceDevice.IsBlocked || !sourceDevice.IsTrusted)
            throw new UnauthorizedAccessException("Sync source device is not allowed.");

        if (!string.Equals(delta.DeviceId, BuildDeviceId(delta.SignPub), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Sync source device id is invalid.");

        return sourceDevice;
    }


    private async Task ValidateDeviceIdentityImmutabilityAsync(Device sourceDevice, SyncDeltaPayload payload, CancellationToken ct)
    {
        if (payload.ModelType != SyncModelType.Device || payload.Device is null)
            return;

        if (payload.Device.SignPublicKey.Length != PasswordManagerLocalBackend.Constants.SyncConstants.SyncDeltaEd25519PublicKeyBytes)
            throw new InvalidDataException("Device sync signing key is invalid.");

        if (payload.Device.PublicKey.Length != PasswordManagerLocalBackend.Constants.SyncConstants.SyncDeltaX25519PublicKeyBytes)
            throw new InvalidDataException("Device sync agreement key is invalid.");

        if (string.IsNullOrWhiteSpace(payload.Device.TlsCertFingerprint))
            throw new InvalidDataException("Device sync TLS fingerprint is missing.");

        if (!DeviceTypeDetector.IsValid(payload.Device.DeviceType))
            throw new InvalidDataException("Device sync type is invalid.");

        if (payload.ModelId == sourceDevice.Id)
        {
            if (!payload.Device.SignPublicKey.SequenceEqual(sourceDevice.SignPublicKey))
                throw new InvalidDataException("Source device signing key cannot be changed by sync.");

            if (!payload.Device.PublicKey.SequenceEqual(sourceDevice.PublicKey))
                throw new InvalidDataException("Source device agreement key cannot be changed by sync.");

            if (!string.Equals(FingerprintUtil.NormalizeOrEmpty(payload.Device.TlsCertFingerprint), FingerprintUtil.NormalizeOrEmpty(sourceDevice.TlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source device TLS fingerprint cannot be changed by sync.");

            if (payload.Device.DeviceType != sourceDevice.DeviceType)
                throw new InvalidDataException("Source device type cannot be changed by sync.");
        }

        var existing = await _devices.GetByIdAsync(payload.ModelId, ct);
        if (existing is null)
            return;

        if (!existing.SignPublicKey.SequenceEqual(payload.Device.SignPublicKey))
            throw new InvalidDataException("Existing device signing key cannot be changed by sync.");

        if (!existing.PublicKey.SequenceEqual(payload.Device.PublicKey))
            throw new InvalidDataException("Existing device agreement key cannot be changed by sync.");

        if (!string.Equals(FingerprintUtil.NormalizeOrEmpty(existing.TlsCertFingerprint), FingerprintUtil.NormalizeOrEmpty(payload.Device.TlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Existing device TLS fingerprint cannot be changed by sync.");

        if (existing.DeviceType != payload.Device.DeviceType)
            throw new InvalidDataException("Existing device type cannot be changed by sync.");
    }


    private async Task ValidateSourceCanApplyPayloadAsync(Device sourceDevice, SyncDeltaPayload payload, CancellationToken ct)
    {
        if (payload.ModelType == SyncModelType.User)
        {
            if (await _userDevices.HasActiveLinkAsync(payload.ModelId, sourceDevice.Id, ct))
                return;

            throw new UnauthorizedAccessException("Source device cannot modify this user.");
        }

        if (payload.ModelType == SyncModelType.Group)
        {
            var group = await _groups.GetByIdWithUsersAsync(payload.ModelId, ct);
            var userIds = group is not null
                ? group.Users.Select(u => u.UId).ToList()
                : payload.Group?.UserIds ?? [];

            if (userIds.Count == 0)
                throw new UnauthorizedAccessException("Source device cannot modify this group.");

            foreach (var userId in userIds)
            {
                if (await _userDevices.HasActiveLinkAsync(userId, sourceDevice.Id, ct))
                    return;
            }

            throw new UnauthorizedAccessException("Source device cannot modify this group.");
        }

        if (payload.ModelType == SyncModelType.Device)
        {
            if (IsLocalDevicePayload(payload))
                return;

            if (payload.ModelId == sourceDevice.Id)
                return;

            if (await _userDevices.SharesActiveUserAsync(sourceDevice.Id, payload.ModelId, ct))
                return;

            if (payload.Device is not null)
            {
                foreach (var userId in payload.Device.UserIds.Where(id => id != Guid.Empty).Distinct())
                {
                    if (await _userDevices.HasActiveLinkAsync(userId, sourceDevice.Id, ct))
                        return;
                }
            }

            throw new UnauthorizedAccessException("Source device cannot modify this device.");
        }

        if (payload.ModelType == SyncModelType.UserDevice)
        {
            if (payload.UserDevice is null)
                throw new InvalidDataException("User device sync payload is missing.");

            ValidateUserDevicePayload(payload);

            if (await _userDevices.HasActiveLinkAsync(payload.UserDevice.UserId, sourceDevice.Id, ct))
                return;

            throw new UnauthorizedAccessException("Source device cannot modify this user-device link.");
        }
    }


    private async Task TouchSourceDeviceAsync(Device sourceDevice, CancellationToken ct)
    {
        sourceDevice.LastSync = DateTime.UtcNow;
        sourceDevice.LastSeen = DateTime.UtcNow;
        sourceDevice.InvalidSyncAttemptCount = 0;
        sourceDevice.LastInvalidSyncAttemptAt = null;
        sourceDevice.GenerateIntegrityHash();

        await RefreshCachedDeviceAsync(sourceDevice, ct);
    }


    private bool DeletesSourceDevice(SyncDeltaPayload payload, Device sourceDevice) =>
        payload.ModelType == SyncModelType.Device &&
        payload.ChangeType == SyncChangeType.Deleted &&
        payload.ModelId == sourceDevice.Id;


    private bool IsRemoteUserDeviceDeletion(SyncDeltaPayload payload) =>
        payload.ModelType == SyncModelType.UserDevice &&
        payload.ChangeType == SyncChangeType.Deleted &&
        payload.UserDevice is not null &&
        payload.UserDevice.IsDeleted &&
        payload.UserDevice.DeviceId != _identity.LocalDeviceId;


    private bool ShouldPropagate(SyncDeltaPayload payload) =>
        !IsDeletedUserPayload(payload) &&
        !DeletesLocalUserProfile(payload) &&
        !IsLocalDevicePayload(payload);


    private static bool IsDeletedUserPayload(SyncDeltaPayload payload) =>
        payload.ModelType == SyncModelType.User &&
        payload.ChangeType == SyncChangeType.Deleted;


    private bool DeletesLocalUserProfile(SyncDeltaPayload payload) =>
        payload.ModelType == SyncModelType.UserDevice &&
        payload.ChangeType == SyncChangeType.Deleted &&
        payload.UserDevice is not null &&
        payload.UserDevice.IsDeleted &&
        payload.UserDevice.DeviceId == _identity.LocalDeviceId;


    private UserSyncPayload CreateUserSyncPayloadForHash(User user) =>
        new()
        {
            UId = user.UId,
            UsernameHash = user.UsernameHash,
            UsernameSalt = user.UsernameSalt,
            PasswordSalt = user.PasswordSalt,
            EncryptedPayload = user.EncryptedPayload,
            EncryptedGeneralUserDataPayload = user.EncryptedGeneralUserDataPayload,
            EncryptedUserPasswordsDataPayload = user.EncryptedUserPasswordsDataPayload,
            EncryptedUserDevicesDataPayload = user.EncryptedUserDevicesDataPayload,
            UserDataLastModifiedAt = user.UserDataLastModifiedAt,
            GeneralUserDataLastModifiedAt = user.GeneralUserDataLastModifiedAt,
            UserPasswordsDataLastModifiedAt = user.UserPasswordsDataLastModifiedAt,
            UserDevicesDataLastModifiedAt = user.UserDevicesDataLastModifiedAt,
            GroupIds = user.Groups.Select(g => g.Id).Distinct().ToList(),
            DeviceIds = user.UserDevices.Where(ud => !ud.IsDeleted).Select(ud => ud.DeviceId).Append(_identity.LocalDeviceId).Distinct().ToList()
        };


    private GroupSyncPayload CreateGroupSyncPayloadForHash(Group group) =>
        new()
        {
            Id = group.Id,
            EncryptedPayload = group.EncryptedPayload,
            UserIds = group.Users.Select(u => u.UId).Distinct().ToList()
        };


    private DeviceSyncPayload CreateDeviceSyncPayloadForHash(Device device) =>
        new()
        {
            Id = device.Id,
            PublicKey = device.PublicKey,
            SignPublicKey = device.SignPublicKey,
            TlsCertFingerprint = device.TlsCertFingerprint,
            DeviceType = device.DeviceType,
            LastKnownHash = device.LastKnownHash,
            LastSync = device.LastSync,
            LastSeen = device.LastSeen,
            IsTrusted = device.IsTrusted,
            IsBlocked = device.IsBlocked,
            BlockedReason = device.BlockedReason,
            BlockedAt = device.BlockedAt,
            InvalidSyncAttemptCount = device.InvalidSyncAttemptCount,
            LastInvalidSyncAttemptAt = device.LastInvalidSyncAttemptAt,
            UserIds = device.UserDevices.Where(ud => !ud.IsDeleted).Select(ud => ud.UserId).Distinct().ToList()
        };


    private bool IsLocalDevicePayload(SyncDeltaPayload payload) =>
        payload.ModelType == SyncModelType.Device &&
        (payload.ModelId == _identity.LocalDeviceId || IsLocalDevicePayload(payload.Device));


    private bool IsLocalDevicePayload(DeviceSyncPayload? device) =>
        device is not null &&
        (device.Id == _identity.LocalDeviceId ||
         device.SignPublicKey.SequenceEqual(_identity.SignPublicKey) ||
         string.Equals(FingerprintUtil.NormalizeOrEmpty(device.TlsCertFingerprint), FingerprintUtil.NormalizeOrEmpty(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase));


    private async Task RefreshCachedDeviceAsync(Device device, CancellationToken ct)
    {
        _syncDeviceIdentities.TryRemove(device);

        if (!device.IsTrusted || device.IsBlocked)
            return;

        if (!await _authorization.HasEligibleUserForDeviceAsync(device.Id, ct))
            return;

        if (await _syncQueue.HasPendingForDeviceAsync(device.Id, ct))
            _syncDeviceIdentities.TryAdd(device);
    }


    private async Task<bool> IsAlreadyAppliedAsync(SyncDeltaPayload payload, long ts, CancellationToken ct)
    {
        if (payload.ChangeType == SyncChangeType.Deleted && payload.ModelType != SyncModelType.UserDevice)
        {
            var tombstone = await _tombstones.GetAsync(payload.ModelId, payload.ModelType, ct);
            return tombstone is not null && tombstone.DeletedAtTs >= ts;
        }

        if (payload.ModelType == SyncModelType.User)
        {
            if (payload.User is null || payload.User.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                return false;

            var existing = await _users.GetByIdWithRelationsAsync(payload.ModelId, ct);
            if (existing is null)
                return false;

            var existingPayload = CreateUserSyncPayloadForHash(existing);
            var existingHash = SyncCryptoUtil.CalculateUserHash(existingPayload, existing.LastModifiedAt.ToUnixTimeMilliseconds());
            return existingHash.SequenceEqual(payload.User.IntegrityHash);
        }

        if (payload.ModelType == SyncModelType.Group)
        {
            if (payload.Group is null || payload.Group.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                return false;

            var existing = await _groups.GetByIdWithUsersAsync(payload.ModelId, ct);
            if (existing is null)
                return false;

            var existingPayload = CreateGroupSyncPayloadForHash(existing);
            var existingHash = SyncCryptoUtil.CalculateGroupHash(existingPayload, existing.LastModifiedAt.ToUnixTimeMilliseconds());
            return existingHash.SequenceEqual(payload.Group.IntegrityHash);
        }

        if (payload.ModelType == SyncModelType.Device)
        {
            if (payload.Device is null || payload.Device.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                return false;

            var existing = await _devices.GetByIdWithUsersAsync(payload.ModelId, ct);
            if (existing is null)
                return false;

            var existingPayload = CreateDeviceSyncPayloadForHash(existing);
            var existingHash = SyncCryptoUtil.CalculateDeviceHash(existingPayload, existing.LastModifiedAt.ToUnixTimeMilliseconds());
            return existingHash.SequenceEqual(payload.Device.IntegrityHash);
        }

        if (payload.ModelType == SyncModelType.UserDevice)
        {
            if (payload.UserDevice is null || payload.UserDevice.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                return false;

            var existing = await _userDevices.GetAsync(payload.UserDevice.UserId, payload.UserDevice.DeviceId, ct);
            if (existing is null)
                return false;

            existing.VerifyIntegrity();
            return existing.IntegrityHash.SequenceEqual(payload.UserDevice.IntegrityHash);
        }

        return false;
    }


    private Task PropagateIncomingDeltaAsync(SyncDeltaPayload payload, Guid sourceDeviceId, long changedAtTs, CancellationToken ct) =>
        _syncQueueService.EnqueuePropagationAsync(new SyncItem
        {
            ModelId = payload.ModelId,
            ModelType = payload.ModelType,
            ChangeType = payload.ChangeType,
            ChangedAtTs = changedAtTs
        }, sourceDeviceId, changedAtTs, ct);


    private async Task<bool> IsBlockedByNewerTombstoneAsync(SyncDeltaPayload payload, long ts, CancellationToken ct)
    {
        var tombstone = await _tombstones.GetAsync(payload.ModelId, payload.ModelType, ct);
        return tombstone is not null && tombstone.DeletedAtTs >= ts;
    }


    private async Task RemoveTombstoneAsync(SyncDeltaPayload payload, CancellationToken ct)
    {
        var tombstone = await _tombstones.GetAsync(payload.ModelId, payload.ModelType, ct);
        if (tombstone is not null)
            _tombstones.Delete(tombstone);
    }


    private User CreateUser(UserSyncPayload payload) =>
        new()
        {
            UId = payload.UId
        };


    private Group CreateGroup(GroupSyncPayload payload) =>
        new()
        {
            Id = payload.Id
        };


    private Device CreateDevice(DeviceSyncPayload payload) =>
        new()
        {
            Id = payload.Id
        };


    private void CopyUserData(UserSyncPayload source, User target)
    {
        target.UId = source.UId;
        target.UsernameHash = source.UsernameHash;
        target.UsernameSalt = source.UsernameSalt;
        target.PasswordSalt = source.PasswordSalt;
        target.EncryptedPayload = source.EncryptedPayload;
        target.EncryptedGeneralUserDataPayload = source.EncryptedGeneralUserDataPayload;
        target.EncryptedUserPasswordsDataPayload = source.EncryptedUserPasswordsDataPayload;
        target.EncryptedUserDevicesDataPayload = source.EncryptedUserDevicesDataPayload;
        target.UserDataLastModifiedAt = source.UserDataLastModifiedAt;
        target.GeneralUserDataLastModifiedAt = source.GeneralUserDataLastModifiedAt;
        target.UserPasswordsDataLastModifiedAt = source.UserPasswordsDataLastModifiedAt;
        target.UserDevicesDataLastModifiedAt = source.UserDevicesDataLastModifiedAt;
        target.IntegrityHash = source.IntegrityHash;
    }


    private void CopyGroupData(GroupSyncPayload source, Group target)
    {
        target.Id = source.Id;
        target.EncryptedPayload = source.EncryptedPayload;
        target.IntegrityHash = source.IntegrityHash;
    }


    private void CopyDeviceData(DeviceSyncPayload source, Device target, bool isNew)
    {
        target.Id = source.Id;
        target.PublicKey = source.PublicKey;
        target.SignPublicKey = source.SignPublicKey;
        target.TlsCertFingerprint = source.TlsCertFingerprint;
        target.DeviceType = source.DeviceType;
        target.LastKnownHash = source.LastKnownHash;
        target.IntegrityHash = source.IntegrityHash;

        if (isNew)
        {
            target.LastSync = source.LastSync;
            target.LastSeen = source.LastSeen;
            target.IsTrusted = source.IsTrusted;
            target.IsBlocked = source.IsBlocked;
            target.BlockedReason = source.BlockedReason;
            target.BlockedAt = source.BlockedAt;
            target.InvalidSyncAttemptCount = source.InvalidSyncAttemptCount;
            target.LastInvalidSyncAttemptAt = source.LastInvalidSyncAttemptAt;
            return;
        }

        if (source.IsBlocked)
        {
            target.IsBlocked = true;
            target.BlockedReason = source.BlockedReason;
            target.BlockedAt = source.BlockedAt ?? DateTimeOffset.UtcNow;
        }
    }


    private byte[] DecryptPayload(NetworkDelta delta)
    {
        try
        {
            var associatedData = SyncCryptoUtil.BuildAssociatedData(delta);
            var plaintext = _identity.DecryptFromDevice(
                delta.Payload,
                delta.EphemeralPublicKey,
                delta.Nonce,
                delta.Tag,
                associatedData);

            SyncCryptoUtil.ValidatePlaintextHash(plaintext, delta.PayloadHash);
            return plaintext;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("Network delta payload decryption failed.", ex);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Network delta encryption key material is invalid.", ex);
        }
    }


    private SyncDeltaPayload DeserializePayload(byte[] plaintextPayload)
    {
        if (plaintextPayload.Length == 0)
            throw new InvalidDataException("Network delta payload is empty.");

        RejectSensitiveLocalOnlyPayload(plaintextPayload);

        var payload = JsonSerializer.Deserialize<SyncDeltaPayload>(plaintextPayload);
        if (payload is null)
            throw new InvalidDataException("Network delta payload is invalid.");

        return payload;
    }


    private void ValidateEnvelope(NetworkDelta delta, SyncDeltaPayload payload)
    {
        if (delta.Ts <= 0)
            throw new InvalidDataException("Network delta timestamp is invalid.");

        if (payload.ModelId == Guid.Empty)
            throw new InvalidDataException("Network delta model id is invalid.");

        if (!Enum.IsDefined(payload.ModelType))
            throw new InvalidDataException("Network delta model type is invalid.");

        if (!Enum.IsDefined(payload.ChangeType))
            throw new InvalidDataException("Network delta change type is invalid.");

        var expectedEntity = $"{payload.ModelType}:{payload.ChangeType}:{payload.ModelId:N}";
        if (!string.Equals(delta.Entity, expectedEntity, StringComparison.Ordinal))
            throw new InvalidDataException("Network delta entity envelope is invalid.");

        if (payload.User is not null && (payload.ModelType != SyncModelType.User || payload.User.UId != payload.ModelId))
            throw new InvalidDataException("User sync payload envelope is invalid.");

        if (payload.Group is not null && (payload.ModelType != SyncModelType.Group || payload.Group.Id != payload.ModelId))
            throw new InvalidDataException("Group sync payload envelope is invalid.");

        if (payload.Device is not null && (payload.ModelType != SyncModelType.Device || payload.Device.Id != payload.ModelId))
            throw new InvalidDataException("Device sync payload envelope is invalid.");

        if (payload.UserDevice is not null)
        {
            if (payload.ModelType != SyncModelType.UserDevice)
                throw new InvalidDataException("User device sync payload envelope is invalid.");

            var expectedModelId = SyncIdentityUtil.BuildUserDeviceModelId(payload.UserDevice.UserId, payload.UserDevice.DeviceId);
            if (payload.ModelId != expectedModelId)
                throw new InvalidDataException("User device sync model id is invalid.");
        }
    }


    private void ValidateUserDevicePayload(SyncDeltaPayload payload)
    {
        if (payload.UserDevice is null)
            throw new InvalidDataException("User device sync payload is missing.");

        if (payload.UserDevice.UserId == Guid.Empty || payload.UserDevice.DeviceId == Guid.Empty)
            throw new InvalidDataException("User device sync payload contains an invalid id.");

        var expectedModelId = SyncIdentityUtil.BuildUserDeviceModelId(payload.UserDevice.UserId, payload.UserDevice.DeviceId);
        if (payload.ModelId != expectedModelId)
            throw new InvalidDataException("User device sync model id is invalid.");

        if (payload.ChangeType == SyncChangeType.Deleted && !payload.UserDevice.IsDeleted)
            throw new InvalidDataException("Deleted user-device delta must contain a deleted link payload.");

        if (payload.ChangeType == SyncChangeType.Deleted && payload.UserDevice.IsSyncOn)
            throw new InvalidDataException("Deleted user-device delta cannot keep synchronization enabled.");

        if (payload.ChangeType == SyncChangeType.Deleted && payload.UserDevice.DeletedAt is null)
            throw new InvalidDataException("Deleted user-device delta must contain deletion time.");

        if (payload.UserDevice.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
            throw new InvalidDataException("User device sync hash is missing.");
    }


    private void VerifyDeltaSignature(NetworkDelta delta)
    {
        if (delta.SignPub.Length != PasswordManagerLocalBackend.Constants.SyncConstants.SyncDeltaEd25519PublicKeyBytes ||
            delta.Sig.Length != PasswordManagerLocalBackend.Constants.SyncConstants.SyncDeltaEd25519SignatureBytes)
            throw new InvalidDataException("Network delta signature is incomplete.");

        try
        {
            if (!NetDeltaSigner.VerifySignature(delta))
                throw new InvalidDataException("Network delta signature is invalid.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Network delta signature is invalid.", ex);
        }
    }


    private void RejectSensitiveLocalOnlyPayload(byte[] payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            foreach (var propertyName in SensitiveLocalOnlyPropertyNames)
            {
                if (ContainsProperty(doc.RootElement, propertyName))
                    throw new InvalidDataException("Network delta contains local-only device or key material.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Network delta payload is invalid.", ex);
        }
    }


    private static readonly string[] SensitiveLocalOnlyPropertyNames =
    [
        "SavedKey",
        "LocalDeviceIdentity",
        "LocalUserDevice",
        "LocalUserDevices",
        "DeviceIdentity",
        "AgreementPrivateKeyBlob",
        "SignPrivateKeyBlob",
        "PFXCertificate",
        "PrivateKey",
        "PrivateKeyBlob"
    ];


    private bool ContainsProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (ContainsProperty(property.Value, propertyName))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsProperty(item, propertyName))
                    return true;
            }
        }

        return false;
    }


    private string BuildDeviceId(byte[] signPublicKey) =>
        Convert.ToHexString(Hashing.SHA256Hash(signPublicKey));




    private DateTimeOffset FromTimestamp(long ts) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ts);


    private bool IsIncomingOlderOrSame(DateTimeOffset local, long incomingTs) =>
        local.ToUnixTimeMilliseconds() >= incomingTs;


    private HashSet<Guid> CreateIdSet(IEnumerable<Guid>? ids) =>
        ids?.Where(id => id != Guid.Empty).Distinct().ToHashSet() ?? [];
}

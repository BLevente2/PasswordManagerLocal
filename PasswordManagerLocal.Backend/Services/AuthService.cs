using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Security;
using System.Security.Cryptography;
using System.Text;
using static PasswordManagerLocal.Backend.Utils.DataCodec;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Services;

public sealed class AuthService : IAuthService
{
    private readonly IUserLookupService _userLookup;
    private readonly IUserDataReaderService _userDataReader;
    private readonly IUserDataWriterService _userDataWriter;
    private readonly IUserSessionService _userSessions;
    private readonly ITokenService _tokens;
    private readonly IDataCachingService _cache;
    private readonly IKeyVaultService _keys;
    private readonly IRememberMeService _rememberMe;
    private readonly IDeviceIdentityService _identity;
    private readonly ILocalUserDeviceRepository _localUserDevices;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly IUserDataBundleIntegrityService _integrity;
    private readonly IUnitOfWork _uow;
    private readonly IUserSnapshotMergeCoordinator _snapshotMerge;
    private readonly IUserRepository _users;
    private readonly IUserSyncSnapshotRepository _snapshots;
    private readonly IUserLifecycleCoordinator _lifecycle;
    private readonly IUserControlOperationWriterService _controlWriter;
    private readonly IUserSnapshotPublisherService _snapshotPublisher;
    private readonly ISyncQueueWriterService _queueWriter;
    private readonly IPendingSyncActivationService _activation;
    private readonly IUserMembershipAuthorizationService _membershipAuthorization;
    private readonly IUserControlStateRepository _controlStates;
    private readonly IUserSyncStateRepository _syncStates;
    private readonly ISyncVersionClockService _versionClock;
    private readonly IUserTombstoneGarbageCollector? _garbageCollector;
    private readonly IUserLoginIdentityProjectionService _loginIdentities;
    private readonly IUserCanonicalHealthService? _canonicalHealth;
    private readonly IUserDataRecoveryCoordinator? _recoveryCoordinator;

    public AuthService(
        IUserLookupService userLookup,
        IUserDataReaderService userDataReader,
        IUserDataWriterService userDataWriter,
        IUserSessionService userSessions,
        ITokenService tokens,
        IRememberMeService rememberMe,
        IDataCachingService cache,
        IKeyVaultService keys,
        IDeviceIdentityService identity,
        ILocalUserDeviceRepository localUserDevices,
        ISyncRuntimeService syncRuntime,
        IUserDataBundleIntegrityService integrity,
        IUnitOfWork uow,
        IUserSnapshotMergeCoordinator snapshotMerge,
        IUserRepository users,
        IUserSyncSnapshotRepository snapshots,
        IUserLifecycleCoordinator lifecycle,
        IUserControlOperationWriterService controlWriter,
        IUserSnapshotPublisherService snapshotPublisher,
        ISyncQueueWriterService queueWriter,
        IPendingSyncActivationService activation,
        IUserMembershipAuthorizationService membershipAuthorization,
        IUserControlStateRepository controlStates,
        IUserSyncStateRepository syncStates,
        ISyncVersionClockService versionClock,
        IUserLoginIdentityProjectionService loginIdentities,
        IUserTombstoneGarbageCollector? garbageCollector = null,
        IUserCanonicalHealthService? canonicalHealth = null,
        IUserDataRecoveryCoordinator? recoveryCoordinator = null)
    {
        _userLookup = userLookup;
        _userDataReader = userDataReader;
        _userDataWriter = userDataWriter;
        _userSessions = userSessions;
        _tokens = tokens;
        _rememberMe = rememberMe;
        _cache = cache;
        _keys = keys;
        _identity = identity;
        _localUserDevices = localUserDevices;
        _syncRuntime = syncRuntime;
        _integrity = integrity;
        _uow = uow;
        _snapshotMerge = snapshotMerge;
        _users = users;
        _snapshots = snapshots;
        _lifecycle = lifecycle;
        _controlWriter = controlWriter;
        _snapshotPublisher = snapshotPublisher;
        _queueWriter = queueWriter;
        _activation = activation;
        _membershipAuthorization = membershipAuthorization;
        _controlStates = controlStates;
        _syncStates = syncStates;
        _versionClock = versionClock;
        _loginIdentities = loginIdentities;
        _garbageCollector = garbageCollector;
        _canonicalHealth = canonicalHealth;
        _recoveryCoordinator = recoveryCoordinator;
    }



    public async Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default)
    {
        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var usernameBytes = Encoding.UTF8.GetBytes(request.Username);
        await ThrowIfUsernameExistsAsync(usernameBytes, ct);

        var now = DateTime.UtcNow;
        var linkedAt = DateTimeOffset.UtcNow;
        var bundle = CreateInitialUserDataBundle(request, now, linkedAt);

        var passwordSalt = Hashing.GenerateSalt();
        using var key = EncryptionKey.FromPassword(request.Password, passwordSalt);
        var user = await CreateEncryptedUserForRegistrationAsync(bundle, usernameBytes, passwordSalt, key, linkedAt, ct);

        await SaveRegisteredUserAsync(user, request.RememberMe, key, ct);
        return CreateAuthenticatedSession(user.UId, key, bundle);
    }


    private async Task ThrowIfUsernameExistsAsync(byte[] usernameBytes, CancellationToken ct)
    {
        var resolution = await _userLookup.ResolveUsernameAsync(usernameBytes, ct);
        if (resolution.State != UserLoginIdentityMatchState.NotFound)
            throw new InvalidInputException();
    }


    private UserDataBundle CreateInitialUserDataBundle(
        RegistrationRequest request,
        DateTime now,
        DateTimeOffset linkedAt)
    {
        var userData = new UserData { UId = Guid.NewGuid() };
        UserDataKeyUtil.InitializeUserDataKeys(userData);

        var bundle = new UserDataBundle
        {
            UserData = userData,
            GeneralUserData = CreateInitialGeneralUserData(request, now),
            UserPasswordsData = CreateInitialUserPasswordsData(),
            UserDevicesData = CreateInitialUserDevicesData(now, linkedAt)
        };
        _integrity.RebuildInitialIntegrity(bundle);
        return bundle;
    }


    private GeneralUserData CreateInitialGeneralUserData(RegistrationRequest request, DateTime now) =>
        new()
        {
            Username = request.Username,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            RegistrationDate = now,
            LastUpdatedAt = now,
            Version = _versionClock.Next()
        };


    private UserPasswordsData CreateInitialUserPasswordsData()
    {
        var userPasswordsData = new UserPasswordsData();
        using var passwordsKey = EncryptionKey.Create();
        userPasswordsData.PasswordKey = passwordsKey.ExportCopy();
        return userPasswordsData;
    }


    private UserDevicesData CreateInitialUserDevicesData(DateTime now, DateTimeOffset linkedAt)
    {
        var version = _versionClock.Next();
        var localDeviceData = new UserDeviceData
        {
            Id = _identity.LocalDeviceId,
            Name = DeviceNameUtil.BuildDefaultDeviceName(_identity.LocalDeviceId),
            LinkedAt = linkedAt,
            LastLoginDate = now,
            LastUpdatedAt = linkedAt,
            Version = version
        };
        localDeviceData.GenerateIntegrityHash();

        var userDevicesData = new UserDevicesData();
        userDevicesData.Devices.Add(localDeviceData);
        return userDevicesData;
    }


    private async Task<User> CreateEncryptedUserForRegistrationAsync(
        UserDataBundle bundle,
        byte[] usernameBytes,
        byte[] passwordSalt,
        EncryptionKey key,
        DateTimeOffset linkedAt,
        CancellationToken ct)
    {
        var usernameSalt = Hashing.GenerateSalt();
        var usernameHash = Hashing.SHA256Hash(usernameBytes, usernameSalt);
        var userData = bundle.UserData;

        var userDataTask = SerializeCompressEncryptAsync(userData, key, BackendJsonSerializerContext.Default.UserData, ct: ct);
        var generalTask = EncryptGeneralUserDataAsync(bundle.GeneralUserData, userData.GeneralUserDataKey, ct);
        var passwordsTask = EncryptUserPasswordsDataAsync(bundle.UserPasswordsData, userData.UserPasswordsDataKey, ct);
        var devicesTask = EncryptUserDevicesDataAsync(bundle.UserDevicesData, userData.UserDevicesDataKey, ct);
        var encryptionTasks = new[] { userDataTask, generalTask, passwordsTask, devicesTask };

        try
        {
            await Task.WhenAll(encryptionTasks);

            return new User
            {
                UId = userData.UId,
                UsernameSalt = usernameSalt,
                UsernameHash = usernameHash,
                PasswordSalt = passwordSalt,
                EncryptedPayload = await userDataTask,
                EncryptedGeneralUserDataPayload = await generalTask,
                EncryptedUserPasswordsDataPayload = await passwordsTask,
                EncryptedUserDevicesDataPayload = await devicesTask,
                UserDataLastModifiedAt = linkedAt,
                GeneralUserDataLastModifiedAt = linkedAt,
                UserPasswordsDataLastModifiedAt = linkedAt,
                UserDevicesDataLastModifiedAt = linkedAt,
                GeneralDataVersionPhysicalTimeUnixMilliseconds = bundle.GeneralUserData.Version.PhysicalTimeUnixMilliseconds,
                GeneralDataVersionLogicalCounter = bundle.GeneralUserData.Version.LogicalCounter,
                GeneralDataVersionOriginDeviceId = bundle.GeneralUserData.Version.OriginDeviceId,
                GeneralDataVersionOriginInstanceId = bundle.GeneralUserData.Version.OriginInstanceId
            };
        }
        catch
        {
            CryptographicOperations.ZeroMemory(usernameSalt);
            CryptographicOperations.ZeroMemory(usernameHash);
            foreach (var task in encryptionTasks)
                ZeroCompletedEncryptionTask(task);
            throw;
        }
    }


    private async Task SaveRegisteredUserAsync(
        User user,
        bool rememberMe,
        EncryptionKey key,
        CancellationToken ct)
    {
        await using var transaction = await _uow.BeginTransactionAsync(ct);
        try
        {
            user.KeyEpoch = 1;
            user.MembershipEpoch = 1;
            user.GenerateIntegrityHash();
            _rememberMe.SetRememberMe(user, rememberMe, key);
            await _userDataWriter.AddNewUserAsync(user, ct);
            await _loginIdentities.SetCanonicalAsync(user, user.GetGeneralUserDataVersion(), ct);
            await AddLocalUserDeviceLinkAsync(user.UId, ct);
            await _membershipAuthorization.CreateGenesisAsync(user, ct);
            await _controlStates.AddAsync(new UserControlState
            {
                UserId = user.UId,
                LocalOriginInstanceId = _identity.OriginInstanceId,
                NextOriginSequence = 1,
                AppliedKeyEpoch = user.KeyEpoch,
                AppliedMembershipEpoch = user.MembershipEpoch,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, ct);
            await _syncStates.AddAsync(new UserSyncState
            {
                UserId = user.UId,
                LocalOriginInstanceId = _identity.OriginInstanceId,
                NextOriginRevision = 1,
                LastPublishedContentHash = [],
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            }, ct);
            await _uow.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _uow.ClearTrackedChanges();
            throw;
        }
        await _syncRuntime.RefreshSyncEnabledAsync(ct);
    }


    private Task AddLocalUserDeviceLinkAsync(Guid userId, CancellationToken ct) =>
        _localUserDevices.AddAsync(new LocalUserDevice
        {
            UserId = userId,
            LocalDeviceIdentityId = _identity.LocalDeviceId,
            IsSyncOn = true
        }, ct);


    private Guid CreateAuthenticatedSession(Guid userId, EncryptionKey key, UserDataBundle bundle)
    {
        var token = _tokens.Issue(userId);
        _keys.SetUserKey(token, key);
        _keys.SetUserBlobKeys(token, bundle.UserData);
        _cache.SetUserDataBundle(token, bundle);
        return token;
    }


    public async Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (!request.Validate())
            throw new InvalidInputException();

        var usernameBytes = Encoding.UTF8.GetBytes(request.Username);
        try
        {
            var initialResolution = await _userLookup.ResolveUsernameAsync(usernameBytes, ct);
            if (initialResolution.State != UserLoginIdentityMatchState.Matched || !initialResolution.UserId.HasValue)
                throw new UserNotFoundException();

            var userId = initialResolution.UserId.Value;
            return await _lifecycle.ExecuteAsync(
                userId,
                token => LoginResolvedUnderLifecycleAsync(request, usernameBytes, userId, token),
                ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(usernameBytes);
        }
    }

    private async Task<Guid> LoginResolvedUnderLifecycleAsync(
        LoginRequest request,
        byte[] usernameBytes,
        Guid expectedUserId,
        CancellationToken ct)
    {
        var prePasswordResolution = await _userLookup.ResolveUsernameAsync(usernameBytes, ct);
        if (prePasswordResolution.State != UserLoginIdentityMatchState.Matched ||
            prePasswordResolution.UserId != expectedUserId)
            throw new UsernameChangedDuringLoginException();

        var user = await _userLookup.GetUserByUidAsync(expectedUserId, ct)
                   ?? throw new UserNotFoundException();
        byte[]? authenticatedRecoverySalt = null;
        try
        {
            try
            {
                user.VerifyIntegrity();
            }
            catch (InvalidDataIntegrityException) when (_recoveryCoordinator is not null)
            {
                // A damaged canonical row must not block recovery before the supplied password can
                // be tested against authenticated current-epoch evidence. The salt resolver never
                // trusts the damaged row and returns a value only when all eligible signed evidence
                // agrees on one historically authorized salt.
                authenticatedRecoverySalt = await _recoveryCoordinator.TryResolvePasswordSaltAsync(user.UId, ct);
            }

            var passwordSalt = authenticatedRecoverySalt ?? user.PasswordSalt;
            if (passwordSalt.Length != Hashing.SHA256HashSizeInBytes)
                throw new UnauthorizedAccessException("Authentication failed.");

            using var key = EncryptionKey.FromPassword(request.Password, passwordSalt);

            await _snapshotMerge.TryMergePendingUnderLifecycleAsync(user.UId, key, UserSyncKeyConfidence.UnconfirmedPassword, ct);
            if (_recoveryCoordinator is not null)
            {
                await _recoveryCoordinator.TryRecoverAsync(
                    user.UId,
                    key,
                    UserSyncKeyConfidence.UnconfirmedPassword,
                    UserDataRecoveryTrigger.Login,
                    ct);
            }
            if (_garbageCollector is not null)
            {
                try
                {
                    await _garbageCollector.CollectAsync(user.UId, key, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Login must remain available when conservative maintenance cannot complete.
                }
            }

            user = await _userLookup.GetAndVerifyUserByUidAsync(user.UId, ct);
            var bundle = await _userDataReader.GetAndVerifyUserDataBundleAsync(user, key, ct);
            try
            {
                UserLoginIdentityMetadataUtil.Verify(user, bundle.GeneralUserData);
                if (_canonicalHealth is not null)
                {
                    var health = await _canonicalHealth.VerifyAsync(
                        user, key, UserSyncKeyConfidence.ExplicitlyTrusted, recordFault: true, ct: ct);
                    if (health.State is not (UserDataVerificationState.Healthy or UserDataVerificationState.CheckpointMissing))
                        throw new UnauthorizedAccessException("The local account copy could not be verified.");
                }

                var postMergeResolution = await _userLookup.ResolveUsernameAsync(usernameBytes, ct);
                if (postMergeResolution.State != UserLoginIdentityMatchState.Matched ||
                    postMergeResolution.UserId != expectedUserId)
                    throw new UsernameChangedDuringLoginException();

                var modifiedBlobs = UserDataBlobKind.None;
                UserDeviceLoginUtil.UpdateCurrentDeviceLastLoginDate(
                    bundle.UserDevicesData,
                    _identity.LocalDeviceId,
                    DateTimeOffset.UtcNow,
                    _versionClock.Next());
                modifiedBlobs |= UserDataBlobKind.Devices;
                _rememberMe.SetRememberMe(user, request.RememberMe, key);
                await _userDataWriter.UpdateUserDataBundleAsync(bundle, user, key, modifiedBlobs, true, ct);

                var finalResolution = await _userLookup.ResolveUsernameAsync(usernameBytes, ct);
                if (finalResolution.State != UserLoginIdentityMatchState.Matched ||
                    finalResolution.UserId != expectedUserId)
                    throw new UsernameChangedDuringLoginException();

                return CreateAuthenticatedSession(user.UId, key, bundle);
            }
            catch
            {
                bundle.Dispose();
                throw;
            }
        }
        finally
        {
            if (authenticatedRecoverySalt is not null)
                CryptographicOperations.ZeroMemory(authenticatedRecoverySalt);
        }
    }




    public Task<Guid> RenewSessionAsync(Guid token, CancellationToken ct = default)
    {
        if (!_tokens.TryGetUid(token, out var uid))
            throw new InvalidTokenException();

        if (!_keys.TryGetEncryptionKey(token, out var key))
        {
            InvalidateToken(token, AuthSessionInvalidationReason.Expired);
            throw new InvalidTokenException();
        }

        try
        {
            var newToken = _tokens.Issue(uid);
            _keys.SetUserKey(newToken, key);

            if (_cache.TryGetUserDataBundle(token, out var bundle) && bundle is not null)
            {
                _keys.SetUserBlobKeys(newToken, bundle.UserData);
                _cache.SetUserDataBundle(newToken, bundle);
            }

            InvalidateToken(token, AuthSessionInvalidationReason.LoggedOut);
            return Task.FromResult(newToken);
        }
        finally
        {
            key.Dispose();
        }
    }


    public void Logout(Guid token)
    {
        if (!_tokens.Validate(token))
            throw new InvalidTokenException();

        InvalidateToken(token, AuthSessionInvalidationReason.LoggedOut);
    }


    public void LogoutUser(Guid uid) =>
        LogoutUser(uid, AuthSessionInvalidationReason.LoggedOut);


    public void LogoutUser(Guid uid, AuthSessionInvalidationReason reason)
    {
        foreach (var token in _tokens.ListTokensByUid(uid))
            InvalidateToken(token, reason);
    }


    public AuthSessionStatusResponse GetSessionStatus(Guid token)
    {
        if (_tokens.TryGetUid(token, out _) && _tokens.TryGetExpiresAtUtc(token, out var expiresAtUtc))
        {
            return new AuthSessionStatusResponse
            {
                IsAuthenticated = true,
                InvalidationReason = AuthSessionInvalidationReason.None,
                ExpiresAtUtc = expiresAtUtc
            };
        }

        var reason = _tokens.TryGetInvalidationReason(token, out var foundReason)
            ? foundReason
            : AuthSessionInvalidationReason.Expired;

        return new AuthSessionStatusResponse
        {
            IsAuthenticated = false,
            InvalidationReason = reason
        };
    }


    public async Task RefreshSyncedUserSessionsAsync(User user, CancellationToken ct = default)
    {
        foreach (var token in _tokens.ListTokensByUid(user.UId))
        {
            if (!_keys.TryGetEncryptionKey(token, out var key))
            {
                InvalidateToken(token, AuthSessionInvalidationReason.Expired);
                continue;
            }

            try
            {
                var bundle = await _userDataReader.GetAndVerifyUserDataBundleAsync(user, key, ct);
                _keys.SetUserBlobKeys(token, bundle.UserData);
                _cache.SetUserDataBundle(token, bundle);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                InvalidateToken(token, AuthSessionInvalidationReason.ProfilePasswordChanged);
            }
            finally
            {
                key.Dispose();
            }
        }
    }


    private void InvalidateToken(Guid token, AuthSessionInvalidationReason reason)
    {
        _cache.InvalidateToken(token);
        _keys.InvalidateToken(token);
        _tokens.Revoke(token, reason);
    }


    public async Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default)
    {
        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var initialUser = await _userLookup.GetAndVerifyUserAsync(request.Token, ct);
        await _lifecycle.ExecuteAsync(
            initialUser.UId,
            token => ChangeMasterPasswordCoreAsync(request, initialUser.UId, token),
            ct);
    }

    private async Task ChangeMasterPasswordCoreAsync(
        MasterPasswordChangeRequest request,
        Guid userId,
        CancellationToken ct)
    {
        var currentUser = await _userLookup.GetAndVerifyUserAsync(request.Token, ct);
        if (currentUser.UId != userId || !IsPasswordValid(request.Token, request.Password, currentUser.PasswordSalt))
            throw new InvalidInputException();

        using (var currentKey = _userSessions.GetEncryptionKeyFromToken(request.Token))
            await _snapshotMerge.TryMergePendingAsync(userId, currentKey, UserSyncKeyConfidence.AuthenticatedSession, ct);

        var user = await _userLookup.GetAndVerifyUserByUidAsync(userId, ct);
        if (await _snapshots.HasQuarantinedAsync(user.UId, user.KeyEpoch, user.MembershipEpoch, ct))
        {
            throw new InvalidOperationException(
                "The master password cannot be changed while a current-epoch snapshot origin is quarantined or unresolved.");
        }

        // Tombstone causal references target the next snapshot in the epoch where the deletion
        // was authored. Publish the current canonical bytes before rotating the key so no local
        // deletion anchor can be orphaned by the epoch transition. The replacement snapshot then
        // carries this old-epoch merged knowledge forward in its authenticated coverage vector.
        await _snapshotPublisher.GetOrCreateAsync(user, ct);

        _cache.InvalidateToken(request.Token);
        var bundle = await _userDataReader.GetLoadAndVerifyUserDataBundleAsync(request.Token, ct, user);
        var canonicalBackup = CanonicalUserState.Capture(user);
        var previousKeyEpoch = user.KeyEpoch;
        var rememberMeWasEnabled = user.SavedKey is not null;
        if (user.SavedKey is not null)
            CryptographicOperations.ZeroMemory(user.SavedKey);
        user.SavedKey = null;

        CryptographicOperations.ZeroMemory(user.PasswordSalt);
        user.PasswordSalt = Hashing.GenerateSalt();
        user.KeyEpoch = checked(previousKeyEpoch + 1);
        using var newKey = EncryptionKey.FromPassword(request.NewPassword, user.PasswordSalt);

        await using var transaction = await _uow.BeginTransactionAsync(ct);
        try
        {
            await _userDataWriter.ReencryptUserDataBundleWithNewKeysAsync(
                bundle,
                user,
                newKey,
                enqueueSync: false,
                ct);

            await _controlWriter.CreateAppliedKeyEpochReplacementAsync(user, previousKeyEpoch, ct);
            var publishingUser = await _users.GetByIdWithRelationsAsync(user.UId, ct)
                ?? throw new InvalidOperationException("The canonical user disappeared during password rotation.");
            await _snapshotPublisher.GetOrCreateAsync(publishingUser, ct);
            await _queueWriter.EnqueueAsync(
                new SyncItem
                {
                    ModelId = user.UId,
                    ModelType = SyncModelType.User,
                    ChangeType = SyncChangeType.Updated
                },
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                [],
                touchLocalSyncState: true,
                activateTargets: false,
                ct);

            await _uow.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            canonicalBackup.Restore(user);
            _uow.ClearTrackedChanges();
            canonicalBackup.ZeroCopies();
            throw;
        }

        Exception? postCommitFailure = null;
        try
        {
            // In-memory key/session state cannot move ahead of the committed canonical transition.
            if (!_keys.RotateUserKey(request.Token, newKey))
                throw new InvalidOperationException("The initiating session is no longer active.");
            _keys.SetUserBlobKeys(request.Token, bundle.UserData);
            _cache.SetUserDataBundle(request.Token, bundle);

            if (rememberMeWasEnabled)
            {
                _rememberMe.SetRememberMe(user, true, newKey);
                await _userDataWriter.UpdateSavedKeyOnlyAsync(user, ct);
            }
        }
        catch (Exception ex)
        {
            // Canonical state is already committed. Fail closed instead of leaving the initiating
            // token associated with an old or partially refreshed in-memory key state. A failed
            // local SavedKey write must also not remain pending in the scoped EF change tracker.
            if (user.SavedKey is not null)
                CryptographicOperations.ZeroMemory(user.SavedKey);
            user.SavedKey = null;
            _uow.ClearTrackedChanges();
            InvalidateToken(request.Token, AuthSessionInvalidationReason.ProfilePasswordChanged);
            postCommitFailure = ex;
        }
        finally
        {
            foreach (var otherToken in _tokens.ListTokensByUid(user.UId))
            {
                if (otherToken != request.Token)
                    InvalidateToken(otherToken, AuthSessionInvalidationReason.ProfilePasswordChanged);
            }

            try
            {
                // Durable queue/control rows were committed above. Activation is only a wake-up
                // optimization; a failure here must not turn a committed password change into a
                // rollback or remove retryable work.
                await _activation.ActivatePendingAsync(CancellationToken.None);
            }
            catch
            {
            }

            canonicalBackup.ZeroCopies();
        }

        if (postCommitFailure is not null)
        {
            throw new InvalidOperationException(
                "The master password was changed durably, but the local authenticated session could not be refreshed.",
                postCommitFailure);
        }
    }


    public bool IsPasswordValid(Guid token, byte[] password, byte[] salt)
    {
        using var currentKey = _userSessions.GetEncryptionKeyFromToken(token);
        using var confirmationKey = EncryptionKey.FromPassword(password, salt);
        return currentKey == confirmationKey;
    }


    public bool TryGetActiveUserEncryptionKey(Guid uid, out EncryptionKey? key)
    {
        foreach (var token in _tokens.ListTokensByUid(uid))
        {
            if (_keys.TryGetEncryptionKey(token, out key))
                return true;
        }

        key = null;
        return false;
    }

    private async Task<byte[]> EncryptGeneralUserDataAsync(GeneralUserData data, byte[] rawKey, CancellationToken ct)
    {
        using var key = EncryptionKey.FromRaw(rawKey);
        return await SerializeCompressEncryptAsync(data, key, BackendJsonSerializerContext.Default.GeneralUserData, ct: ct);
    }

    private async Task<byte[]> EncryptUserPasswordsDataAsync(UserPasswordsData data, byte[] rawKey, CancellationToken ct)
    {
        using var key = EncryptionKey.FromRaw(rawKey);
        return await SerializeCompressEncryptAsync(data, key, BackendJsonSerializerContext.Default.UserPasswordsData, ct: ct);
    }

    private async Task<byte[]> EncryptUserDevicesDataAsync(UserDevicesData data, byte[] rawKey, CancellationToken ct)
    {
        using var key = EncryptionKey.FromRaw(rawKey);
        return await SerializeCompressEncryptAsync(data, key, BackendJsonSerializerContext.Default.UserDevicesData, ct: ct);
    }


    private void ZeroCompletedEncryptionTask(Task<byte[]> task)
    {
        if (task.Status == TaskStatus.RanToCompletion)
            CryptographicOperations.ZeroMemory(task.Result);
    }

    private sealed class CanonicalUserState
    {
        private readonly byte[] _usernameHash;
        private readonly byte[] _usernameSalt;
        private readonly byte[] _passwordSalt;
        private readonly byte[] _encryptedPayload;
        private readonly byte[] _encryptedGeneralPayload;
        private readonly byte[] _encryptedPasswordsPayload;
        private readonly byte[] _encryptedDevicesPayload;
        private readonly byte[]? _savedKey;
        private readonly byte[] _integrityHash;
        private readonly long _keyEpoch;
        private readonly long _membershipEpoch;
        private readonly long _generalVersionPhysicalTimeUnixMilliseconds;
        private readonly long _generalVersionLogicalCounter;
        private readonly Guid _generalVersionOriginDeviceId;
        private readonly Guid _generalVersionOriginInstanceId;
        private readonly DateTimeOffset _lastModifiedAt;
        private readonly DateTimeOffset _userDataLastModifiedAt;
        private readonly DateTimeOffset _generalLastModifiedAt;
        private readonly DateTimeOffset _passwordsLastModifiedAt;
        private readonly DateTimeOffset _devicesLastModifiedAt;

        private CanonicalUserState(User user)
        {
            _usernameHash = user.UsernameHash.ToArray();
            _usernameSalt = user.UsernameSalt.ToArray();
            _passwordSalt = user.PasswordSalt.ToArray();
            _encryptedPayload = user.EncryptedPayload.ToArray();
            _encryptedGeneralPayload = user.EncryptedGeneralUserDataPayload.ToArray();
            _encryptedPasswordsPayload = user.EncryptedUserPasswordsDataPayload.ToArray();
            _encryptedDevicesPayload = user.EncryptedUserDevicesDataPayload.ToArray();
            _savedKey = user.SavedKey?.ToArray();
            _integrityHash = user.IntegrityHash.ToArray();
            _keyEpoch = user.KeyEpoch;
            _membershipEpoch = user.MembershipEpoch;
            _generalVersionPhysicalTimeUnixMilliseconds = user.GeneralDataVersionPhysicalTimeUnixMilliseconds;
            _generalVersionLogicalCounter = user.GeneralDataVersionLogicalCounter;
            _generalVersionOriginDeviceId = user.GeneralDataVersionOriginDeviceId;
            _generalVersionOriginInstanceId = user.GeneralDataVersionOriginInstanceId;
            _lastModifiedAt = user.LastModifiedAt;
            _userDataLastModifiedAt = user.UserDataLastModifiedAt;
            _generalLastModifiedAt = user.GeneralUserDataLastModifiedAt;
            _passwordsLastModifiedAt = user.UserPasswordsDataLastModifiedAt;
            _devicesLastModifiedAt = user.UserDevicesDataLastModifiedAt;
        }

        public static CanonicalUserState Capture(User user) => new(user);

        public void Restore(User user)
        {
            ZeroIfDifferent(user.UsernameHash, _usernameHash);
            ZeroIfDifferent(user.UsernameSalt, _usernameSalt);
            ZeroIfDifferent(user.PasswordSalt, _passwordSalt);
            ZeroIfDifferent(user.EncryptedPayload, _encryptedPayload);
            ZeroIfDifferent(user.EncryptedGeneralUserDataPayload, _encryptedGeneralPayload);
            ZeroIfDifferent(user.EncryptedUserPasswordsDataPayload, _encryptedPasswordsPayload);
            ZeroIfDifferent(user.EncryptedUserDevicesDataPayload, _encryptedDevicesPayload);
            if (user.SavedKey is not null && !ReferenceEquals(user.SavedKey, _savedKey))
                CryptographicOperations.ZeroMemory(user.SavedKey);

            user.UsernameHash = _usernameHash.ToArray();
            user.UsernameSalt = _usernameSalt.ToArray();
            user.PasswordSalt = _passwordSalt.ToArray();
            user.EncryptedPayload = _encryptedPayload.ToArray();
            user.EncryptedGeneralUserDataPayload = _encryptedGeneralPayload.ToArray();
            user.EncryptedUserPasswordsDataPayload = _encryptedPasswordsPayload.ToArray();
            user.EncryptedUserDevicesDataPayload = _encryptedDevicesPayload.ToArray();
            user.SavedKey = _savedKey?.ToArray();
            user.IntegrityHash = _integrityHash.ToArray();
            user.KeyEpoch = _keyEpoch;
            user.MembershipEpoch = _membershipEpoch;
            user.GeneralDataVersionPhysicalTimeUnixMilliseconds = _generalVersionPhysicalTimeUnixMilliseconds;
            user.GeneralDataVersionLogicalCounter = _generalVersionLogicalCounter;
            user.GeneralDataVersionOriginDeviceId = _generalVersionOriginDeviceId;
            user.GeneralDataVersionOriginInstanceId = _generalVersionOriginInstanceId;
            user.LastModifiedAt = _lastModifiedAt;
            user.UserDataLastModifiedAt = _userDataLastModifiedAt;
            user.GeneralUserDataLastModifiedAt = _generalLastModifiedAt;
            user.UserPasswordsDataLastModifiedAt = _passwordsLastModifiedAt;
            user.UserDevicesDataLastModifiedAt = _devicesLastModifiedAt;
        }

        public void ZeroCopies()
        {
            CryptographicOperations.ZeroMemory(_usernameHash);
            CryptographicOperations.ZeroMemory(_usernameSalt);
            CryptographicOperations.ZeroMemory(_passwordSalt);
            CryptographicOperations.ZeroMemory(_encryptedPayload);
            CryptographicOperations.ZeroMemory(_encryptedGeneralPayload);
            CryptographicOperations.ZeroMemory(_encryptedPasswordsPayload);
            CryptographicOperations.ZeroMemory(_encryptedDevicesPayload);
            CryptographicOperations.ZeroMemory(_integrityHash);
            if (_savedKey is not null)
                CryptographicOperations.ZeroMemory(_savedKey);
        }

        private static void ZeroIfDifferent(byte[] current, byte[] backup)
        {
            if (!ReferenceEquals(current, backup))
                CryptographicOperations.ZeroMemory(current);
        }
    }
}

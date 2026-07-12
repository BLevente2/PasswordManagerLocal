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
        IUnitOfWork uow)
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
        var foundUser = await _userLookup.GetUserByUsernameAsync(usernameBytes, ct);
        if (foundUser is not null)
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
            LastUpdatedAt = now
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
        var localDeviceData = new UserDeviceData
        {
            Id = _identity.LocalDeviceId,
            Name = DeviceNameUtil.BuildDefaultDeviceName(_identity.LocalDeviceId),
            LinkedAt = linkedAt,
            LastLoginDate = now,
            LastUpdatedAt = linkedAt
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
                UserDevicesDataLastModifiedAt = linkedAt
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
        _rememberMe.SetRememberMe(user, rememberMe, key);
        await _userDataWriter.AddNewUserAsync(user, ct);
        await AddLocalUserDeviceLinkAsync(user.UId, ct);
        await _uow.SaveChangesAsync(ct);
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
        var user = await _userLookup.GetAndVerifyUserByUsernameAsync(usernameBytes, ct);
        using var key = EncryptionKey.FromPassword(request.Password, user.PasswordSalt);

        var bundle = await _userDataReader.GetAndVerifyUserDataBundleAsync(user, key, ct);
        var modifiedBlobs = TombstoneCleanupUtil.CleanupExpiredUserDataTombstones(bundle, DateTimeOffset.UtcNow);
        UpdateCurrentDeviceLastLoginDate(bundle.UserDevicesData);
        modifiedBlobs |= UserDataBlobKind.Devices;
        _rememberMe.SetRememberMe(user, request.RememberMe, key);
        await _userDataWriter.UpdateUserDataBundleAsync(bundle, user, key, modifiedBlobs, true, ct);

        return CreateAuthenticatedSession(user.UId, key, bundle);
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

        var user = await _userLookup.GetAndVerifyUserAsync(request.Token, ct);

        if (!IsPasswordValid(request.Token, request.Password, user.PasswordSalt))
            throw new InvalidInputException();

        var bundle = await _userDataReader.GetLoadAndVerifyUserDataBundleAsync(request.Token, ct, user);

        CryptographicOperations.ZeroMemory(user.PasswordSalt);
        user.PasswordSalt = Hashing.GenerateSalt();
        using var newKey = EncryptionKey.FromPassword(request.NewPassword, user.PasswordSalt);
        _keys.RotateUserKey(request.Token, newKey);

        if (user.SavedKey is not null)
            _rememberMe.SetRememberMe(user, true, newKey);

        await _userDataWriter.ReencryptUserDataBundleWithNewKeysAsync(bundle, user, newKey, true, ct);
        _keys.SetUserBlobKeys(request.Token, bundle.UserData);
        _cache.SetUserDataBundle(request.Token, bundle);

        foreach (var otherToken in _tokens.ListTokensByUid(user.UId))
        {
            if (otherToken != request.Token)
                InvalidateToken(otherToken, AuthSessionInvalidationReason.ProfilePasswordChanged);
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

    private void UpdateCurrentDeviceLastLoginDate(UserDevicesData userDevicesData)
    {
        var device = userDevicesData.Devices.FirstOrDefault(device => device.Id == _identity.LocalDeviceId);
        if (device is null)
        {
            device = new UserDeviceData
            {
                Id = _identity.LocalDeviceId,
                Name = DeviceNameUtil.BuildDefaultDeviceName(_identity.LocalDeviceId),
                LinkedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow
            };
            userDevicesData.DeletedDevices.RemoveAll(deleted => deleted.Id == device.Id);
            userDevicesData.Devices.Add(device);
        }

        userDevicesData.DeletedDevices.RemoveAll(deleted => deleted.Id == device.Id);
        device.LastLoginDate = DateTime.UtcNow;
        device.LastUpdatedAt = DateTimeOffset.UtcNow;
        device.GenerateIntegrityHash();
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
}

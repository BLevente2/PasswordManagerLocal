using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using PasswordManagerLocalBackend.Security;
using System.Security.Cryptography;
using System.Text;
using static PasswordManagerLocalBackend.Utils.DataCodec;
using PasswordManagerLocalBackend.Utils;

namespace PasswordManagerLocalBackend.Services;

public sealed class AuthService : IAuthService
{
    private readonly IUserService _userService;
    private readonly ITokenService _tokens;
    private readonly IDataCachingService _cache;
    private readonly IKeyVaultService _keys;
    private readonly IRememberMeService _rememberMe;
    private readonly IDeviceIdentityService _identity;
    private readonly ILocalUserDeviceRepository _localUserDevices;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly IUnitOfWork _uow;

    public AuthService(
        IUserService userService,
        ITokenService tokens,
        IRememberMeService rememberMe,
        IDataCachingService cache,
        IKeyVaultService keys,
        IDeviceIdentityService identity,
        ILocalUserDeviceRepository localUserDevices,
        ISyncRuntimeService syncRuntime,
        IUnitOfWork uow)
    {
        _userService = userService;
        _tokens = tokens;
        _rememberMe = rememberMe;
        _cache = cache;
        _keys = keys;
        _identity = identity;
        _localUserDevices = localUserDevices;
        _syncRuntime = syncRuntime;
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
        var foundUser = await _userService.GetUserByUsernameAsync(usernameBytes, ct);
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
        GenerateAndCopyIntegrityHashes(bundle);
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
        var userData = bundle.UserData;
        return new User
        {
            UId = userData.UId,
            UsernameSalt = usernameSalt,
            UsernameHash = Hashing.SHA256Hash(usernameBytes, usernameSalt),
            PasswordSalt = passwordSalt,
            EncryptedPayload = await SerializeCompressEncryptAsync(userData, key, BackendJsonSerializerContext.Default.UserData, ct: ct),
            EncryptedGeneralUserDataPayload = await EncryptGeneralUserDataAsync(bundle.GeneralUserData, userData.GeneralUserDataKey, ct),
            EncryptedUserPasswordsDataPayload = await EncryptUserPasswordsDataAsync(bundle.UserPasswordsData, userData.UserPasswordsDataKey, ct),
            EncryptedUserDevicesDataPayload = await EncryptUserDevicesDataAsync(bundle.UserDevicesData, userData.UserDevicesDataKey, ct),
            UserDataLastModifiedAt = linkedAt,
            GeneralUserDataLastModifiedAt = linkedAt,
            UserPasswordsDataLastModifiedAt = linkedAt,
            UserDevicesDataLastModifiedAt = linkedAt
        };
    }


    private async Task SaveRegisteredUserAsync(
        User user,
        bool rememberMe,
        EncryptionKey key,
        CancellationToken ct)
    {
        _rememberMe.SetRememberMe(user, rememberMe, key);
        await _userService.AddNewUserAsync(user, ct);
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
        var user = await _userService.GetAndVerifyUserByUsernameAsync(usernameBytes, ct);

        var token = _tokens.Issue(user.UId);
        using var key = EncryptionKey.FromPassword(request.Password, user.PasswordSalt);
        _keys.SetUserKey(token, key);

        var bundle = await _userService.GetAndVerifyUserDataBundleAsync(user, token, ct);
        var modifiedBlobs = TombstoneCleanupUtil.CleanupExpiredUserDataTombstones(bundle, DateTimeOffset.UtcNow);
        UpdateCurrentDeviceLastLoginDate(bundle.UserDevicesData);
        modifiedBlobs |= UserDataBlobKind.Devices;
        _rememberMe.SetRememberMe(user, request.RememberMe, key);
        await _userService.UpdateUserDataBundleAsync(bundle, user, key, modifiedBlobs, true, ct);
        _keys.SetUserBlobKeys(token, bundle.UserData);
        _cache.SetUserDataBundle(token, bundle);
        return token;
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
                var bundle = await _userService.GetAndVerifyUserDataBundleAsync(user, key, ct);
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

        var user = await _userService.GetAndVerifyUserAsync(request.Token, ct);

        if (!IsPasswordValid(request.Token, request.Password, user.PasswordSalt))
            throw new InvalidInputException();

        var bundle = await _userService.GetLoadAndVerifyUserDataBundleAsync(request.Token, ct, user);

        CryptographicOperations.ZeroMemory(user.PasswordSalt);
        user.PasswordSalt = Hashing.GenerateSalt();
        using var newKey = EncryptionKey.FromPassword(request.NewPassword, user.PasswordSalt);
        _keys.RotateUserKey(request.Token, newKey);

        if (user.SavedKey is not null)
            _rememberMe.SetRememberMe(user, true, newKey);

        await _userService.ReencryptUserDataBundleWithNewKeysAsync(bundle, user, newKey, true, ct);
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
        using var currentKey = _userService.GetEncryptionKeyFromToken(token);
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
        userDevicesData.GenerateIntegrityHash();
    }

    private void GenerateAndCopyIntegrityHashes(UserDataBundle bundle)
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
        foreach (var tag in bundle.UserPasswordsData.Tags)
            tag.GenerateIntegrityHash();
        foreach (var deleted in bundle.UserPasswordsData.DeletedTags)
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
}

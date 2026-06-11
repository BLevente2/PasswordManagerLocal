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

namespace PasswordManagerLocalBackend.Services;

public sealed class AuthService : IAuthService
{
    private readonly IUserService _userService;
    private readonly ITokenService _tokens;
    private readonly IDataCachingService _cache;
    private readonly IKeyVaultService _keys;
    private readonly IRememberMeService _rememberMe;

    public AuthService(
        IUserService userService,
        ITokenService tokens,
        IRememberMeService rememberMe,
        IDataCachingService cache,
        IKeyVaultService keys)
    {
        _userService = userService;
        _tokens = tokens;
        _rememberMe = rememberMe;
        _cache = cache;
        _keys = keys;
    }



    public async Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default)
    {
        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var usernameBytes = Encoding.UTF8.GetBytes(request.Username);
        var foundUser = await _userService.GetUserByUsernameAsync(usernameBytes, ct);
        if (foundUser is not null)
            throw new InvalidInputException();

        var userData = new UserData
        {
            UId = Guid.NewGuid(),
            Username = request.Username,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            RegistrationDate = DateTime.UtcNow,
            LastLoginDate = DateTime.UtcNow
        };

        using var passwordsKey = EncryptionKey.Create();
        userData.Passwords.PasswordKey = passwordsKey.ExportCopy();
        userData.Passwords.GenerateIntegrityHash();
        userData.GenerateIntegrityHash();

        var usernameSalt = Hashing.GenerateSalt();
        var usernameHash = Hashing.SHA256Hash(usernameBytes, usernameSalt);

        var passwordSalt = Hashing.GenerateSalt();
        using var key = EncryptionKey.FromPassword(request.Password, passwordSalt);
        var encryptedUserdata = await SerializeCompressEncryptAsync<UserData>(userData, key);

        var user = new User
        {
            UId = userData.UId,
            UsernameSalt = usernameSalt,
            UsernameHash = usernameHash,
            PasswordSalt = passwordSalt,
            EncryptedPayload = encryptedUserdata
        };

        _rememberMe.SetRememberMe(user, request.RememberMe, key);
        await _userService.AddNewUserAsync(user, ct);

        var token = _tokens.Issue(userData.UId);
        _keys.SetUserKey(token, key);
        _cache.SetUserData(token, userData);

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

        var userData = await _userService.GetAndVerifyUserDataAsync(user, key);
        userData.LastLoginDate = DateTime.UtcNow;
        _rememberMe.SetRememberMe(user, request.RememberMe, key);
        await _userService.UpdateUserDataAsync(userData, user, key, true, ct);
        _cache.SetUserData(token, userData);
        return token;
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
        if (_tokens.Validate(token))
        {
            return new AuthSessionStatusResponse
            {
                IsAuthenticated = true,
                InvalidationReason = AuthSessionInvalidationReason.None
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
                var userData = await _userService.GetAndVerifyUserDataAsync(user, key);
                _cache.SetUserData(token, userData);
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

        var userData = await _userService.GetLoadAndVerifyUserDataAsync(request.Token, ct, user);

        CryptographicOperations.ZeroMemory(user.PasswordSalt);
        user.PasswordSalt = Hashing.GenerateSalt();
        using var newKey = EncryptionKey.FromPassword(request.NewPassword, user.PasswordSalt);
        _keys.RotateUserKey(request.Token, newKey);

        if (user.SavedKey is not null)
            _rememberMe.SetRememberMe(user, true, newKey);

        await _userService.UpdateUserDataAsync(userData, user, newKey, true, ct);
    }


    public bool IsPasswordValid(Guid token, byte[] password, byte[] salt)
    {
        using var currentKey = _userService.GetEncryptionKeyFromToken(token);
        using var confirmationKey = EncryptionKey.FromPassword(password, salt);
        return currentKey == confirmationKey;
    }
}
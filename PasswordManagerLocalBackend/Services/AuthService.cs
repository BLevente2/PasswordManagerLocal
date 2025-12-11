using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.DTOs;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using System.Text;
using static PasswordManagerLocalBackend.Utils.DataCodec;

namespace PasswordManagerLocalBackend.Services;

public sealed class AuthService : IAuthService
{
    private readonly IUnitOfWork _uow;
    private readonly IUserService _userService;
    private readonly ITokenService _tokens;
    private readonly IDataCachingService _cache;
    private readonly IKeyVaultService _keys;
    private readonly IRememberMeService _rememberMe;
    private IUserRepository _users;

    public AuthService(IUnitOfWork uow, IUserService userService, ITokenService tokens, IRememberMeService rememberMe, IDataCachingService cache, IKeyVaultService keys, IUserRepository users)
    {
        _uow = uow;
        _userService = userService;
        _tokens = tokens;
        _rememberMe = rememberMe;
        _cache = cache;
        _keys = keys;
        _users = users;
    }




    public async Task<string> RegisterAsync(RegistrationDTO dto, CancellationToken ct = default)
    {
        if (!dto.Validate(out var errors))
            throw new InvalidInputException(errors);

        var usernameBytes = Encoding.UTF8.GetBytes(dto.Username);
        var foundUser = _userService.GetUserByUsernameAsync(usernameBytes, ct);
        if (foundUser is not null)
            throw new InvalidInputException();

        var userData = new UserData
        {
            UId = Guid.NewGuid(),
            Username = dto.Username,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            RegistrationDate = DateTime.UtcNow,
            LastLoginDate = DateTime.UtcNow
        };
        userData.GenerateIntegrityHash();

        var usernameSalt = Hashing.GenerateSalt();
        var usernameHash = Hashing.SHA256Hash(usernameBytes, usernameSalt);

        var passwordSalt = Hashing.GenerateSalt();
        using var key = EncryptionKey.FromPassword(dto.Password, passwordSalt);
        var encryptedUserdata = await SerializeCompressEncryptAsync<UserData>(userData, key);

        var user = new User
        {
            UId = userData.UId,
            UsernameSalt = usernameSalt,
            UsernameHash = usernameHash,
            PasswordSalt = passwordSalt,
            EncryptedPayload = encryptedUserdata
        };
        _rememberMe.SetRememberMe(user, dto.RememberMe, key);
        user.GenerateIntegrityHash();

        await _users.AddAsync(user, ct);
        await _uow.SaveChangesAsync();

        var token = _tokens.Issue();
        _keys.SetUserKey(token, key);
        _cache.SetUserData(token, userData);

        return token;
    }



    public async Task<string> LoginAsync(LoginDTO dto, CancellationToken ct = default)
    {
        if (!dto.Validate())
            throw new InvalidInputException();

        var usernameBytes = Encoding.UTF8.GetBytes(dto.Username);
        var user = await _userService.GetUserByUsernameAsync(usernameBytes, ct);
        if (user is null)
            throw new UserNotFoundException();

        if (!user.IsIntegrityValid())
            throw new InvalidDataIntegrityException(typeof(User));

        var token = _tokens.Issue();
        using var key = EncryptionKey.FromPassword(dto.Password, user.PasswordSalt);
        _keys.SetUserKey(token, key);

        UserData userData = await _userService.GetUserDataAsync(user.UId, token, ct);
        userData.LastLoginDate = DateTime.UtcNow;
        userData.GenerateIntegrityHash();
        _cache.SetUserData(token, userData);
        var encryptedUserData = await SerializeCompressEncryptAsync<UserData>(userData, key);

        user.EncryptedPayload = encryptedUserData;
        _rememberMe.SetRememberMe(user, dto.RememberMe, key);
        user.GenerateIntegrityHash();
        _users.Update(user);

        await _uow.SaveChangesAsync();
        return token;
    }
}
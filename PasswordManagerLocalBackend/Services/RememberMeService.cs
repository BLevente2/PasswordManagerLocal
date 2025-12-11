using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Services;

public class RememberMeService : IRememberMeService
{
    private readonly IUnitOfWork _uow;
    private readonly ITokenService _tokens;
    private readonly IKeyVaultService _keys;
    private readonly IUserRepository _users;
    private readonly IKeyProtector _protector;
    private readonly IUserService _userService;

    public RememberMeService(IUnitOfWork uow, ITokenService tokens, IKeyVaultService keys, IUserRepository users, IKeyProtector protector, IUserService userService)
    {
        _uow = uow;
        _tokens = tokens;
        _keys = keys;
        _users = users;
        _protector = protector;
        _userService = userService;
    }


    public async Task<IReadOnlyList<string>> InicializeAllRememberMeAsync(CancellationToken ct = default)
    {
        var initializedTokens = new List<string>();

        var usersEnabledRM = await _users.GetAllRememberMeEnabledUsersAsync(ct);
        if (usersEnabledRM.Count == 0)
            return initializedTokens;

        foreach (var user in usersEnabledRM)
        {
            if (!user.IsIntegrityValid())
                throw new InvalidDataIntegrityException(typeof(User));

            using var key = EncryptionKey.FromRaw(_protector.Unprotect(user.SavedKey));
            var token = _tokens.Issue();
            _keys.SetUserKey(token, key);
            initializedTokens.Add(token);
        }
        return initializedTokens;
    }


    public async Task SetRememberMeAsync(string token, bool rememberMe, CancellationToken ct = default)
    {
        var user = await _userService.GetUserByTokenAsync(token, ct);
        if (user is null)
            throw new UserNotFoundException();

        if (!user.IsIntegrityValid())
            throw new InvalidDataIntegrityException(typeof(User));

        if (rememberMe)
        {
            if (!_keys.TryGetEncryptionKey(token, out var key))
                throw new InvalidTokenException();

            user.SavedKey = _protector.Protect(key.ExportCopy());
            key.Dispose();
        }
        else if (user.SavedKey is not null)
            user.SavedKey = null;
        else
            return;

        user.GenerateIntegrityHash();
        _users.Update(user);
        await _uow.SaveChangesAsync();
    }

    public void SetRememberMe(User user, bool rememberMe, EncryptionKey key)
    {
        if (rememberMe)
            user.SavedKey = _protector.Protect(key.ExportCopy());
        else
            user.SavedKey = null;
    }
}
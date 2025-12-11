using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Services;

public class RememberMeService : IRememberMeService
{
    private readonly ITokenService _tokens;
    private readonly IKeyVaultService _keys;
    private readonly IUserRepository _users;
    private readonly IKeyProtector _protector;

    public RememberMeService(ITokenService tokens, IKeyVaultService keys, IUserRepository users, IKeyProtector protector)
    {
        _tokens = tokens;
        _keys = keys;
        _users = users;
        _protector = protector;
    }

    public async Task<IReadOnlyList<string>> InicializeAllRememberMeAsync(CancellationToken ct = default)
    {
        var initializedTokens = new List<string>();

        var usersEnabledRM = await _users.GetAllRememberMeEnabledUsersAsync(ct);
        if (usersEnabledRM.Count == 0)
            return initializedTokens;

        foreach (var user in usersEnabledRM)
        {
            using var key = EncryptionKey.FromRaw(_protector.Unprotect(user.SavedKey));
            var token = _tokens.Issue();
            _keys.SetUserKey(token, key);
            initializedTokens.Add(token);
        }
        return initializedTokens;
    }
}
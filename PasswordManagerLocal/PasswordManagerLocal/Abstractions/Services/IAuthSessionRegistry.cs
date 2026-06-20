using PasswordManagerLocal.Services;
using System;
using System.Collections.Generic;

namespace PasswordManagerLocal.Abstractions.Services;

public interface IAuthSessionRegistry
{
    Guid CurrentUserToken { get; set; }
    bool TryAdd(Guid token);
    bool TrySetProfile(Guid token, Guid userId, string displayName, string subtitle, string username, string email);
    bool TryReplaceToken(Guid oldToken, Guid newToken);
    bool TryRemove(Guid token);
    bool ContainsUserId(Guid userId, Guid excludedToken = default);
    AuthSessionProfile? GetSession(Guid token);
    IReadOnlyList<Guid> ListTokens();
    IReadOnlyList<AuthSessionProfile> ListSessions();
}

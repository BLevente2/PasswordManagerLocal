using System;
using System.Collections.Generic;

namespace PasswordManagerLocal.Abstractions.Services;

public interface IAuthSessionRegistry
{
    Guid CurrentUserToken { get; set; }
    bool TryAdd(Guid token);
    bool TryRemove(Guid token);
    IReadOnlyList<Guid> ListTokens();
}
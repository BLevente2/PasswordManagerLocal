using System.Collections.Generic;

namespace PasswordManagerLocal.Abstractions.Services;

public interface IAuthSessionRegistry
{
    string CurrentUserToken { get; set; }
    bool TryAdd(string token);
    bool TryRemove(string token);
    IReadOnlyList<string> ListTokens();
}
using PasswordManagerLocal.Abstractions.Services;
using System.Collections.Generic;
using System.Linq;

namespace PasswordManagerLocal.Services;

public sealed class AuthSessionRegistry : IAuthSessionRegistry
{
    private readonly List<string> _tokens = new();
    private string _selection = string.Empty;

    public string CurrentUserToken
    {
        get => _selection;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                _selection = string.Empty;
                return;
            }

            if (_tokens.Contains(value))
            {
                _selection = value;
            }
            else
            {
                _selection = string.Empty;
            }
        }
    }

    public bool TryAdd(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (!_tokens.Contains(token))
        {
            _tokens.Add(token);
        }

        // akár új, akár már létező token, legyen ez az aktuális user
        CurrentUserToken = token;
        return true;
    }

    public bool TryRemove(string token)
    {
        if (!_tokens.Contains(token))
        {
            return false;
        }

        _tokens.Remove(token);

        if (_tokens.Count > 0)
        {
            CurrentUserToken = _tokens.First();
        }
        else
        {
            CurrentUserToken = string.Empty;
        }

        return true;
    }

    public IReadOnlyList<string> ListTokens() =>
        _tokens.AsReadOnly();
}
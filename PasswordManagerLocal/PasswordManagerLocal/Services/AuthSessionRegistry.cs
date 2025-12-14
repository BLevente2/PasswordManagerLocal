using PasswordManagerLocal.Abstractions.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PasswordManagerLocal.Services;

public sealed class AuthSessionRegistry : IAuthSessionRegistry
{
    private readonly List<Guid> _tokens = new();
    private Guid _selection = Guid.Empty;

    public Guid CurrentUserToken
    {
        get => _selection;
        set
        {
            if (value == Guid.Empty)
            {
                _selection = Guid.Empty;
                return;
            }

            if (_tokens.Contains(value))
                _selection = value;
            else
                _selection = Guid.Empty;
        }
    }

    public bool TryAdd(Guid token)
    {
        if (token == Guid.Empty)
            return false;

        if (!_tokens.Contains(token))
            _tokens.Add(token);

        CurrentUserToken = token;
        return true;
    }

    public bool TryRemove(Guid token)
    {
        if (!_tokens.Contains(token))
            return false;

        _tokens.Remove(token);

        if (_tokens.Count > 0)
            CurrentUserToken = _tokens.First();
        else
            CurrentUserToken = Guid.Empty;

        return true;
    }

    public IReadOnlyList<Guid> ListTokens() =>
        _tokens.AsReadOnly();
}
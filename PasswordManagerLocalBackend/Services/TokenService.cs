using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using static PasswordManagerLocalBackend.Constants.TokenConstrants;

namespace PasswordManagerLocalBackend.Services;

public sealed class TokenService : ITokenService
{
    private readonly ConcurrentDictionary<Guid, TokenEntry> _storage = new();

    public Guid Issue(Guid uid)
    {
        for (int i = 0; i <= NumberOfTokenGenerationRetries; i++)
        {
            var token = GenerateToken();

            if (token == Guid.Empty)
                continue;

            var expiresTicksUtc = DateTime.UtcNow.Add(TokenExpirationTime).Ticks;
            var entry = new TokenEntry(uid, expiresTicksUtc);

            if (_storage.TryAdd(token, entry))
                return token;
        }

        throw new InvalidOperationException("Token generation failed!");
    }

    public bool Validate(Guid token) =>
        TryGetUid(token, out _);

    public bool TryGetUid(Guid token, out Guid uid)
    {
        uid = default;

        if (token == Guid.Empty)
            return false;

        if (!_storage.TryGetValue(token, out var entry))
            return false;

        var nowTicksUtc = DateTime.UtcNow.Ticks;

        if (entry.ExpiresTicksUtc <= nowTicksUtc)
        {
            _storage.TryRemove(token, out _);
            return false;
        }

        uid = entry.Uid;
        return true;
    }

    public Guid GetUidOrThrow(Guid token)
    {
        if (!TryGetUid(token, out var uid))
            throw new InvalidTokenException("Invalid or expired token.");

        return uid;
    }

    public bool Revoke(Guid token)
    {
        if (token == Guid.Empty)
            return false;

        return _storage.TryRemove(token, out _);
    }

    public int PurgeExpired()
    {
        var nowTicksUtc = DateTime.UtcNow.Ticks;
        var removed = 0;

        foreach (var kv in _storage)
        {
            if (kv.Value.ExpiresTicksUtc <= nowTicksUtc && _storage.TryRemove(kv.Key, out _))
                removed++;
        }

        return removed;
    }

    private static Guid GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return new Guid(bytes);
    }

    private readonly struct TokenEntry
    {
        public readonly Guid Uid;
        public readonly long ExpiresTicksUtc;

        public TokenEntry(Guid uid, long expiresTicksUtc)
        {
            Uid = uid;
            ExpiresTicksUtc = expiresTicksUtc;
        }
    }
}
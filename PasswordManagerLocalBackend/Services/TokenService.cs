using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using static PasswordManagerLocalBackend.Constants.TokenConstrants;

namespace PasswordManagerLocalBackend.Services;

public sealed class TokenService : ITokenService
{
    private static readonly TimeSpan InvalidationReasonRetentionTime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, TokenEntry> _storage = new();
    private readonly ConcurrentDictionary<Guid, InvalidationEntry> _invalidations = new();

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
            {
                _invalidations.TryRemove(token, out _);
                return token;
            }
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
            Revoke(token, AuthSessionInvalidationReason.Expired);
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

    public IReadOnlyList<Guid> ListTokensByUid(Guid uid)
    {
        PurgeExpired();

        if (uid == Guid.Empty)
            return [];

        return _storage
            .Where(kv => kv.Value.Uid == uid)
            .Select(kv => kv.Key)
            .ToList();
    }


    public bool Revoke(Guid token) =>
        Revoke(token, AuthSessionInvalidationReason.LoggedOut);


    public bool Revoke(Guid token, AuthSessionInvalidationReason reason)
    {
        if (token == Guid.Empty)
            return false;

        var removed = _storage.TryRemove(token, out _);

        if (reason != AuthSessionInvalidationReason.None)
        {
            _invalidations[token] = new InvalidationEntry(
                reason,
                DateTime.UtcNow.Add(InvalidationReasonRetentionTime).Ticks);
        }

        return removed;
    }


    public bool TryGetInvalidationReason(Guid token, out AuthSessionInvalidationReason reason)
    {
        reason = AuthSessionInvalidationReason.None;

        if (token == Guid.Empty)
            return false;

        if (!_invalidations.TryGetValue(token, out var entry))
            return false;

        if (entry.ExpiresTicksUtc <= DateTime.UtcNow.Ticks)
        {
            _invalidations.TryRemove(token, out _);
            return false;
        }

        reason = entry.Reason;
        return true;
    }


    public int PurgeExpired()
    {
        var nowTicksUtc = DateTime.UtcNow.Ticks;
        var removed = 0;

        foreach (var kv in _storage)
        {
            if (kv.Value.ExpiresTicksUtc <= nowTicksUtc && _storage.TryRemove(kv.Key, out _))
            {
                _invalidations[kv.Key] = new InvalidationEntry(
                    AuthSessionInvalidationReason.Expired,
                    DateTime.UtcNow.Add(InvalidationReasonRetentionTime).Ticks);
                removed++;
            }
        }

        foreach (var kv in _invalidations)
        {
            if (kv.Value.ExpiresTicksUtc <= nowTicksUtc)
                _invalidations.TryRemove(kv.Key, out _);
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

    private readonly struct InvalidationEntry
    {
        public readonly AuthSessionInvalidationReason Reason;
        public readonly long ExpiresTicksUtc;

        public InvalidationEntry(AuthSessionInvalidationReason reason, long expiresTicksUtc)
        {
            Reason = reason;
            ExpiresTicksUtc = expiresTicksUtc;
        }
    }
}

using PasswordManagerLocalBackend.Abstractions.Services;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using static PasswordManagerLocalBackend.Constants.TokenConstrants;
using static PasswordManagerLocalBackend.Security.Hashing;

namespace PasswordManagerLocalBackend.Services;

public sealed class TokenService : ITokenService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _storage = new(StringComparer.Ordinal);



    public string Issue()
    {
        var randomBytes = new byte[TokenLength];

        for (int i = 0; i <= NumberOfTokenGenerationRetries; i++)
        {
            RandomNumberGenerator.Fill(randomBytes);
            var token = Convert.ToBase64String(randomBytes);

            var hashBytes = SHA256Hash(randomBytes);
            var key = Convert.ToBase64String(hashBytes);

            var expires = DateTimeOffset.UtcNow.Add(TokenExpirationTime);

            if (_storage.TryAdd(key, expires))
                return token;
        }

        throw new InvalidOperationException("Token generation failed!");
    }

    public bool Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        byte[] tokenBytes;
        try { tokenBytes = Convert.FromBase64String(token); }
        catch { return false; }

        var key = Convert.ToBase64String(SHA256Hash(tokenBytes));

        if (!_storage.TryGetValue(key, out var expires))
            return false;

        if (expires <= DateTimeOffset.UtcNow)
        {
            _storage.TryRemove(key, out _);
            return false;
        }

        return true;
    }

    public int PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var removed = 0;

        foreach (var kv in _storage)
            if (kv.Value <= now && _storage.TryRemove(kv.Key, out _))
                removed++;

        return removed;
    }
}
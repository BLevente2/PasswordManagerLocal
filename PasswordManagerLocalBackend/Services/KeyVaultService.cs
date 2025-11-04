using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Security;
using System.Collections.Concurrent;
using static PasswordManagerLocalBackend.Constants.TokenConstrants;

namespace PasswordManagerLocalBackend.Services;

public sealed class KeyVaultService : IKeyVaultService
{
    private sealed class Entry
    {
        public EncryptionKey Key;
        public DateTimeOffset ExpiresAt;
        public Entry(EncryptionKey key, DateTimeOffset exp) { Key = key; ExpiresAt = exp; }
    }

    private readonly ConcurrentDictionary<string, Entry> _map = new(StringComparer.Ordinal);

    private static string K(string token) => $"t:{TokenKey.HashToken(token)}";

    public void SetUserKey(string token, EncryptionKey key, DateTimeOffset? expiresAt = null)
    {
        var raw = key.ExportCopy();
        try
        {
            var owned = EncryptionKey.FromRaw(raw);
            var exp = expiresAt ?? DateTimeOffset.UtcNow.Add(TokenExpirationTime);
            _map.AddOrUpdate(K(token), _ => new Entry(owned, exp), (_, old) =>
            {
                old.Key.Dispose();
                return new Entry(owned, exp);
            });
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw);
        }
    }

    public bool RotateUserKey(string token, EncryptionKey newKey, DateTimeOffset? newExpiresAt = null)
    {
        var id = K(token);
        if (!_map.TryGetValue(id, out var entry)) return false;

        var raw = newKey.ExportCopy();
        try
        {
            var owned = EncryptionKey.FromRaw(raw);
            var exp = newExpiresAt ?? entry.ExpiresAt;
            var replacement = new Entry(owned, exp);

            if (_map.TryUpdate(id, replacement, entry))
            {
                entry.Key.Dispose();
                return true;
            }

            owned.Dispose();
            return false;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw);
        }
    }

    public bool HasUserKey(string token)
    {
        return _map.TryGetValue(K(token), out var e) && e.ExpiresAt > DateTimeOffset.UtcNow;
    }

    public bool TryGetEncryptionKey(string token, out EncryptionKey key)
    {
        key = default!;
        if (!_map.TryGetValue(K(token), out var e)) return false;
        if (e.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            InvalidateToken(token);
            return false;
        }

        var raw = e.Key.ExportCopy();
        try
        {
            key = EncryptionKey.FromRaw(raw);
            return true;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(raw);
        }
    }

    public void InvalidateToken(string token)
    {
        if (_map.TryRemove(K(token), out var e))
            e.Key.Dispose();
    }

    public int PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var n = 0;
        foreach (var kv in _map)
        {
            if (kv.Value.ExpiresAt <= now && _map.TryRemove(kv.Key, out var e))
            {
                e.Key.Dispose();
                n++;
            }
        }
        return n;
    }
}
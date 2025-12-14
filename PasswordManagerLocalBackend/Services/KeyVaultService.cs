using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Security;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using static PasswordManagerLocalBackend.Constants.TokenConstrants;

namespace PasswordManagerLocalBackend.Services;

public sealed class KeyVaultService : IKeyVaultService
{
    private sealed class Entry
    {
        public EncryptionKey Key;
        public DateTimeOffset ExpiresAt;

        public Entry(EncryptionKey key, DateTimeOffset exp)
        {
            Key = key;
            ExpiresAt = exp;
        }
    }

    private readonly ConcurrentDictionary<Guid, Entry> _map = new();

    public void SetUserKey(Guid token, EncryptionKey key, DateTimeOffset? expiresAt = null)
    {
        if (token == Guid.Empty)
            return;

        var raw = key.ExportCopy();
        try
        {
            var owned = EncryptionKey.FromRaw(raw);
            var exp = expiresAt ?? DateTimeOffset.UtcNow.Add(TokenExpirationTime);

            while (true)
            {
                if (_map.TryGetValue(token, out var old))
                {
                    var replacement = new Entry(owned, exp);
                    if (_map.TryUpdate(token, replacement, old))
                    {
                        old.Key.Dispose();
                        return;
                    }

                    continue;
                }

                if (_map.TryAdd(token, new Entry(owned, exp)))
                    return;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    public bool RotateUserKey(Guid token, EncryptionKey newKey, DateTimeOffset? newExpiresAt = null)
    {
        if (token == Guid.Empty)
            return false;

        if (!_map.TryGetValue(token, out var entry))
            return false;

        var raw = newKey.ExportCopy();
        try
        {
            var owned = EncryptionKey.FromRaw(raw);
            var exp = newExpiresAt ?? entry.ExpiresAt;
            var replacement = new Entry(owned, exp);

            if (_map.TryUpdate(token, replacement, entry))
            {
                entry.Key.Dispose();
                return true;
            }

            owned.Dispose();
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    public bool HasUserKey(Guid token)
    {
        if (token == Guid.Empty)
            return false;

        return _map.TryGetValue(token, out var e) && e.ExpiresAt > DateTimeOffset.UtcNow;
    }

    public bool TryGetEncryptionKey(Guid token, out EncryptionKey key)
    {
        key = default!;

        if (token == Guid.Empty)
            return false;

        if (!_map.TryGetValue(token, out var e))
            return false;

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
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    public void InvalidateToken(Guid token)
    {
        if (token == Guid.Empty)
            return;

        if (_map.TryRemove(token, out var e))
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
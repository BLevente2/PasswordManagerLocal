using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Utils;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using static PasswordManagerLocalBackend.Constants.TokenConstants;
using PasswordManagerLocalBackend.State;

namespace PasswordManagerLocalBackend.Services;

public sealed class KeyVaultService : IKeyVaultService
{
    private readonly ConcurrentDictionary<Guid, KeyVaultEntry> _map = new();

    public void SetUserKey(Guid token, EncryptionKey key, DateTimeOffset? expiresAt = null)
    {
        if (token == Guid.Empty)
            return;

        var raw = key.ExportCopy();
        try
        {
            var exp = UtcDateTimeUtil.ToUtc(expiresAt ?? DateTimeOffset.UtcNow.Add(LoginTokenExpirationTime));

            while (true)
            {
                if (_map.TryGetValue(token, out var old))
                {
                    var replacement = new KeyVaultEntry(EncryptionKey.FromRaw(raw), exp);
                    CopyBlobKeys(old, replacement);
                    if (_map.TryUpdate(token, replacement, old))
                    {
                        old.Dispose();
                        return;
                    }

                    replacement.Dispose();
                    continue;
                }

                var entry = new KeyVaultEntry(EncryptionKey.FromRaw(raw), exp);
                if (_map.TryAdd(token, entry))
                    return;

                entry.Dispose();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    public void SetUserBlobKeys(Guid token, UserData userData)
    {
        if (token == Guid.Empty)
            return;

        if (!_map.TryGetValue(token, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow)
            return;

        ReplaceBlobKey(ref entry.GeneralUserDataKey, userData.GeneralUserDataKey);
        ReplaceBlobKey(ref entry.UserPasswordsDataKey, userData.UserPasswordsDataKey);
        ReplaceBlobKey(ref entry.UserDevicesDataKey, userData.UserDevicesDataKey);
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
            var exp = UtcDateTimeUtil.ToUtc(newExpiresAt ?? entry.ExpiresAt);
            var replacement = new KeyVaultEntry(owned, exp);
            CopyBlobKeys(entry, replacement);

            if (_map.TryUpdate(token, replacement, entry))
            {
                entry.Dispose();
                return true;
            }

            replacement.Dispose();
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

    public bool TryGetEncryptionKey(Guid token, out EncryptionKey key) =>
        TryGetKey(token, entry => entry.Key, out key);

    public bool TryGetGeneralUserDataKey(Guid token, out EncryptionKey key) =>
        TryGetKey(token, entry => entry.GeneralUserDataKey, out key);

    public bool TryGetUserPasswordsDataKey(Guid token, out EncryptionKey key) =>
        TryGetKey(token, entry => entry.UserPasswordsDataKey, out key);

    public bool TryGetUserDevicesDataKey(Guid token, out EncryptionKey key) =>
        TryGetKey(token, entry => entry.UserDevicesDataKey, out key);

    private bool TryGetKey(Guid token, Func<KeyVaultEntry, EncryptionKey?> selector, out EncryptionKey key)
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

        var source = selector(e);
        if (source is null)
            return false;

        var raw = source.ExportCopy();
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
            e.Dispose();
    }

    public int PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var n = 0;

        foreach (var kv in _map)
        {
            if (kv.Value.ExpiresAt <= now && _map.TryRemove(kv.Key, out var e))
            {
                e.Dispose();
                n++;
            }
        }

        return n;
    }

    private void ReplaceBlobKey(ref EncryptionKey? target, byte[] raw)
    {
        target?.Dispose();
        target = raw.Length == 0 ? null : EncryptionKey.FromRaw(raw);
    }

    private void CopyBlobKeys(KeyVaultEntry source, KeyVaultEntry target)
    {
        CopyBlobKey(source.GeneralUserDataKey, ref target.GeneralUserDataKey);
        CopyBlobKey(source.UserPasswordsDataKey, ref target.UserPasswordsDataKey);
        CopyBlobKey(source.UserDevicesDataKey, ref target.UserDevicesDataKey);
    }

    private void CopyBlobKey(EncryptionKey? source, ref EncryptionKey? target)
    {
        if (source is null)
            return;

        var raw = source.ExportCopy();
        try
        {
            target = EncryptionKey.FromRaw(raw);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }
}

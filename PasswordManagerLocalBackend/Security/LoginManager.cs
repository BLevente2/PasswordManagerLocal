using System.Security.Cryptography;

namespace PasswordManagerLocalBackend.Security;

internal static class LoginManager
{
    #region SessionEntry
    private sealed class SessionEntry : IDisposable
    {
        #region GroupEntry
        private sealed class GroupEntry : IDisposable
        {
            internal byte[] EncryptedBlob { get; }
            internal GroupData CachedGroupData { get; set; }

            internal GroupEntry(byte[] encryptedBlob, GroupData groupData)
            {
                EncryptedBlob = encryptedBlob;
                CachedGroupData = groupData;
            }

            private bool _disposed;

            ~GroupEntry()
            {
                Dispose(disposing: false);
            }

            public void Dispose()
            {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }

            private void Dispose(bool disposing)
            {
                if (_disposed) return;

                CryptographicOperations.ZeroMemory(EncryptedBlob);

                if (disposing)
                {
                    CachedGroupData.Dispose();
                }

                _disposed = true;
            }
        }
        #endregion

        internal byte[] EncryptedBlob { get; }
        internal DateTime LoginTime { get; }
        internal bool RememberMe { get; }
        internal UserData CachedUserData { get; set; }

        private readonly ConcurrentDictionary<Guid, GroupEntry> _groups = new();

        internal bool IsGroupCached(Guid groupId) => _groups.ContainsKey(groupId);

        public async Task AddGroupAsync(Guid groupId, EncryptionKey groupKey, GroupData groupData)
        {
            // Export a copy of the raw group key for wrapping with the runtime key
            var raw = groupKey.ExportCopy();
            try
            {
                var encryptedBlob = await AES256.EncryptAsync(raw, _runtimeKey);
                var groupEntry = new GroupEntry(encryptedBlob, groupData);

                _groups.AddOrUpdate(
                    groupId,
                    groupEntry,
                    (_, old) => { old.Dispose(); return groupEntry; });
            }
            finally
            {
                CryptographicOperations.ZeroMemory(raw);
            }
        }

        internal GroupData GetCachedGroupData(Guid groupId)
        {
            if (_groups.TryGetValue(groupId, out var groupEntry))
            {
                return groupEntry.CachedGroupData;
            }
            else
            {
                throw new KeyNotFoundException("Group not found in the session cache.");
            }
        }

        internal async Task<EncryptionKey> GetGroupEncryptionKeyAsync(Guid groupId)
        {
            if (!_groups.TryGetValue(groupId, out var groupEntry))
                throw new KeyNotFoundException("Group not found in the session cache.");

            var raw = await AES256.DecryptAsync(groupEntry.EncryptedBlob, _runtimeKey);
            try
            {
                var key = EncryptionKey.FromRaw(raw);
                return key; // caller must Dispose()
            }
            finally
            {
                CryptographicOperations.ZeroMemory(raw);
            }
        }

        internal void DeleteCachedGroup(Guid groupId)
        {
            if (_groups.TryRemove(groupId, out var groupEntry))
            {
                groupEntry.Dispose();
            }
            else
            {
                throw new KeyNotFoundException("Group not found in the session cache.");
            }
        }

        internal void ClearGroupCache()
        {
            foreach (var group in _groups.Values)
            {
                group.Dispose();
            }
            _groups.Clear();
        }

        internal SessionEntry(byte[] encryptedBlob, UserData userData, bool rememberMe)
        {
            EncryptedBlob = encryptedBlob;
            CachedUserData = userData;
            RememberMe = rememberMe;
            LoginTime = DateTime.UtcNow;
        }

        private bool _disposed;

        ~SessionEntry()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            CryptographicOperations.ZeroMemory(EncryptedBlob);

            if (disposing)
            {
                CachedUserData.Dispose();

                foreach (var group in _groups.Values)
                {
                    group.Dispose();
                }
            }

            _groups.Clear();
            _disposed = true;
        }
    }
    #endregion

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

    // Ephemeral process-local key used only to wrap cached per-user/group keys in memory.
    // Note: if the process restarts, cached entries cannot be unwrapped (which is good).
    private static readonly EncryptionKey _runtimeKey = new();

    private static readonly ConcurrentDictionary<Guid, SessionEntry> _sessions = new();

    internal static bool IsLoggedIn(Guid uid) => _sessions.ContainsKey(uid);

    internal static async Task LoginAsync(
        Guid uid,
        EncryptionKey userKey,
        UserData userData,
        bool rememberMe = false)
    {
        // Wrap the userKey with the runtime key for storage in the session cache
        var raw = userKey.ExportCopy();
        try
        {
            var encryptedBlob = await AES256.EncryptAsync(raw, _runtimeKey);
            var entry = new SessionEntry(encryptedBlob, userData, rememberMe);

            _sessions.AddOrUpdate(
                uid,
                entry,
                (_, old) => { old.Dispose(); return entry; });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    internal static async Task<EncryptionKey> GetEncryptionKeyAsync(Guid uid)
    {
        if (!_sessions.TryGetValue(uid, out var entry))
            throw new UnauthorizedAccessException("User is not logged in.");

        var raw = await AES256.DecryptAsync(entry.EncryptedBlob, _runtimeKey);
        try
        {
            var key = EncryptionKey.FromRaw(raw);
            return key; // caller must Dispose()
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    internal static UserData GetUserData(Guid uid)
    {
        if (_sessions.TryGetValue(uid, out var entry))
        {
            return entry.CachedUserData;
        }
        else
        {
            throw new UnauthorizedAccessException("User is not logged in.");
        }
    }

    internal static void UpdateCacheUserData(Guid uid, UserData newUserData)
    {
        if (_sessions.TryGetValue(uid, out var entry))
        {
            if (ReferenceEquals(entry.CachedUserData, newUserData))
            {
                return; // No need to update if the same instance is passed
            }

            entry.CachedUserData.Dispose();
            entry.CachedUserData = newUserData;
        }
        else
        {
            throw new UnauthorizedAccessException("User is not logged in.");
        }
    }

    internal static void Logout(Guid uid)
    {
        if (_sessions.TryRemove(uid, out var entry))
        {
            entry.Dispose();
        }
        else
        {
            throw new UnauthorizedAccessException("User is not logged in.");
        }
    }

    internal static bool IsGroupCached(Guid uid, Guid groupId)
    {
        if (_sessions.TryGetValue(uid, out var entry))
        {
            return entry.IsGroupCached(groupId);
        }
        else
        {
            throw new UnauthorizedAccessException("User is not logged in.");
        }
    }

    internal static async Task AddGroupAsync(Guid uid, Guid groupId, EncryptionKey groupKey, GroupData groupData)
    {
        if (_sessions.TryGetValue(uid, out var entry))
        {
            await entry.AddGroupAsync(groupId, groupKey, groupData);
            groupKey.Dispose();
        }
        else
        {
            groupKey.Dispose();
            throw new UnauthorizedAccessException("User is not logged in.");
        }
    }

    internal static GroupData GetCachedGroupData(Guid uid, Guid groupId)
    {
        if (_sessions.TryGetValue(uid, out var entry))
        {
            return entry.GetCachedGroupData(groupId);
        }
        else
        {
            throw new UnauthorizedAccessException("User is not logged in.");
        }
    }

    internal static async Task<EncryptionKey> GetGroupEncryptionKeyAsync(Guid uid, Guid groupId)
    {
        if (_sessions.TryGetValue(uid, out var entry))
        {
            return await entry.GetGroupEncryptionKeyAsync(groupId);
        }
        else
        {
            throw new UnauthorizedAccessException("User is not logged in.");
        }
    }

    internal static void DeleteCachedGroup(Guid uid, Guid groupId)
    {
        if (_sessions.TryGetValue(uid, out var entry))
        {
            entry.DeleteCachedGroup(groupId);
        }
        else
        {
            throw new UnauthorizedAccessException("User is not logged in.");
        }
    }

    internal static void ClearGroupCache(Guid uid)
    {
        if (_sessions.TryGetValue(uid, out var entry))
        {
            entry.ClearGroupCache();
        }
        else
        {
            throw new UnauthorizedAccessException("User is not logged in.");
        }
    }

    internal static void CleanupExpiredSessions()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _sessions)
        {
            var entry = kv.Value;
            if (!entry.RememberMe && now - entry.LoginTime > SessionLifetime)
            {
                if (_sessions.TryRemove(kv.Key, out var removed)) removed.Dispose();
            }
        }
    }
}
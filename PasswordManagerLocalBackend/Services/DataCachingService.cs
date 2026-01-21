using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Security;
using System.Collections.Concurrent;
using static PasswordManagerLocalBackend.Constants.DataCachingConstants;

namespace PasswordManagerLocalBackend.Services;

public sealed class DataCachingService : IDataCachingService
{
    private readonly SafeMemoryCache _cache;
    private readonly ITokenService _tokens;
    private readonly TimeSpan _userTtl;
    private readonly TimeSpan _groupTtl;

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokenCts = new();
    private readonly ConcurrentDictionary<string, object> _currentByKey = new();

    public DataCachingService(SafeMemoryCache cache, ITokenService tokens)
        : this(cache, tokens, UserDataCacheExpirationTime, GroupDataCacheExpirationTime)
    {
    }

    public DataCachingService(SafeMemoryCache cache, ITokenService tokens, TimeSpan userTtl, TimeSpan groupTtl)
    {
        _cache = cache;
        _tokens = tokens;
        _userTtl = userTtl;
        _groupTtl = groupTtl;
    }

    private static string UserKey(Guid token) => $"t:{token:N}:user";
    private static string GroupKey(Guid token, Guid groupId) => $"t:{token:N}:g:{groupId:N}";

    private void DisposeCurrentIfAny(string key)
    {
        if (_currentByKey.TryRemove(key, out var current) && current is IDisposable d)
        {
            try { d.Dispose(); } catch { }
        }
    }

    private void DisposeCurrentAndRemove(string key)
    {
        DisposeCurrentIfAny(key);
        _cache.Remove(key);
    }

    private MemoryCacheEntryOptions EntryOptions(Guid token, TimeSpan ttl)
    {
        if (!_tokenCts.TryGetValue(token, out var cts))
        {
            var candidate = new CancellationTokenSource();
            cts = _tokenCts.GetOrAdd(token, candidate);
            if (!ReferenceEquals(cts, candidate))
                candidate.Dispose();
        }

        var opts = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Size = 1
        };

        opts.AddExpirationToken(new CancellationChangeToken(cts.Token));

        opts.RegisterPostEvictionCallback((key, value, reason, state) =>
        {
            if (key is not string skey)
                return;

            var self = (DataCachingService)state!;

            if (reason == EvictionReason.Replaced)
            {
                if (self._currentByKey.TryGetValue(skey, out var current) && ReferenceEquals(current, value))
                    return;

                if (value is IDisposable rd)
                {
                    try { rd.Dispose(); } catch { }
                }

                return;
            }

            if (value is IDisposable d)
            {
                try { d.Dispose(); } catch { }
            }

            if (self._currentByKey.TryGetValue(skey, out var cur) && ReferenceEquals(cur, value))
            {
                self._currentByKey.TryRemove(skey, out _);
            }
        }, this);

        return opts;
    }

    public async Task<UserData?> GetOrLoadUserDataAsync(Guid token, Func<CancellationToken, Task<UserData?>> loader, CancellationToken ct = default)
    {
        if (!_tokens.Validate(token))
        {
            InvalidateToken(token);
            return default;
        }

        var key = UserKey(token);
        var result = await _cache.GetOrCreateAsync(key, () => loader(ct), EntryOptions(token, _userTtl));

        if (result != null)
            _currentByKey[key] = result;

        return result;
    }

    public Task<UserData?> GetOrLoadUserDataAsync(Guid token, Func<Task<UserData?>> loader)
        => GetOrLoadUserDataAsync(token, _ => loader());

    public bool TryGetUserData(Guid token, out UserData? value)
    {
        if (!_tokens.Validate(token))
        {
            InvalidateToken(token);
            value = default;
            return false;
        }

        var key = UserKey(token);
        var ok = _cache.TryGet(key, out value);

        if (ok && value != null)
            _currentByKey[key] = value;

        return ok;
    }

    public void SetUserData(Guid token, UserData value)
    {
        if (!_tokens.Validate(token))
        {
            InvalidateToken(token);
            return;
        }

        var key = UserKey(token);
        _currentByKey[key] = value;
        _cache.Set(key, value, EntryOptions(token, _userTtl));
    }

    public async Task<GroupData?> GetOrLoadGroupDataAsync(Guid token, Guid groupId, Func<CancellationToken, Task<GroupData?>> loader, CancellationToken ct = default)
    {
        if (!_tokens.Validate(token))
        {
            InvalidateToken(token);
            return default;
        }

        var key = GroupKey(token, groupId);
        var result = await _cache.GetOrCreateAsync(key, () => loader(ct), EntryOptions(token, _groupTtl));

        if (result != null)
            _currentByKey[key] = result;

        return result;
    }

    public Task<GroupData?> GetOrLoadGroupDataAsync(Guid token, Guid groupId, Func<Task<GroupData?>> loader)
        => GetOrLoadGroupDataAsync(token, groupId, _ => loader());

    public bool TryGetGroupData(Guid token, Guid groupId, out GroupData? value)
    {
        if (!_tokens.Validate(token))
        {
            InvalidateToken(token);
            value = default;
            return false;
        }

        var key = GroupKey(token, groupId);
        var ok = _cache.TryGet(key, out value);

        if (ok && value != null)
            _currentByKey[key] = value;

        return ok;
    }

    public void SetGroupData(Guid token, Guid groupId, GroupData value)
    {
        if (!_tokens.Validate(token))
        {
            InvalidateToken(token);
            return;
        }

        var key = GroupKey(token, groupId);
        _currentByKey[key] = value;
        _cache.Set(key, value, EntryOptions(token, _groupTtl));
    }

    public void InvalidateGroup(Guid token, Guid groupId)
    {
        DisposeCurrentAndRemove(GroupKey(token, groupId));
    }

    public void InvalidateToken(Guid token)
    {
        var prefix = $"t:{token:N}:";

        foreach (var key in _currentByKey.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                DisposeCurrentAndRemove(key);
            }
        }

        if (_tokenCts.TryRemove(token, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
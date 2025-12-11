using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
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
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokenCts = new(StringComparer.Ordinal);

    public DataCachingService(SafeMemoryCache cache, ITokenService tokens) : this(cache, tokens, UserDataCacheExpirationTime, GroupDataCacheExpirationTime)
    {

    }

    public DataCachingService(SafeMemoryCache cache, ITokenService tokens, TimeSpan userTtl, TimeSpan groupTtl)
    {
        _cache = cache;
        _tokens = tokens;
        _userTtl = userTtl;
        _groupTtl = groupTtl;
    }

    private string UserKey(string token) => $"t:{TokenKey.HashToken(token)}:user";
    private string GroupKey(string token, Guid groupId) => $"t:{TokenKey.HashToken(token)}:g:{groupId}";

    private MemoryCacheEntryOptions EntryOptions(string token, TimeSpan ttl)
    {
        var k = TokenKey.HashToken(token);
        var cts = _tokenCts.GetOrAdd(k, _ => new CancellationTokenSource());
        var opts = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl, Size = 1 };
        opts.AddExpirationToken(new CancellationChangeToken(cts.Token));
        return opts;
    }

    public async Task<UserData?> GetOrLoadUserDataAsync(string token, Func<CancellationToken, Task<UserData?>> loader, CancellationToken ct = default)
    {
        if (!_tokens.Validate(token)) return default;
        return await _cache.GetOrCreateAsync(UserKey(token), () => loader(ct), EntryOptions(token, _userTtl));
    }

    public Task<UserData?> GetOrLoadUserDataAsync(string token, Func<Task<UserData?>> loader)
        => GetOrLoadUserDataAsync(token, _ => loader());

    public bool TryGetUserData(string token, out UserData? value)
    {
        if (!_tokens.Validate(token)) { value = default; return false; }
        return _cache.TryGet(UserKey(token), out value);
    }

    public void SetUserData(string token, UserData value)
    {
        if (!_tokens.Validate(token)) return;
        _cache.Set(UserKey(token), value, EntryOptions(token, _userTtl));
    }

    public async Task<GroupData?> GetOrLoadGroupDataAsync(string token, Guid groupId, Func<CancellationToken, Task<GroupData?>> loader, CancellationToken ct = default)
    {
        if (!_tokens.Validate(token)) return default;
        return await _cache.GetOrCreateAsync(GroupKey(token, groupId), () => loader(ct), EntryOptions(token, _groupTtl));
    }

    public Task<GroupData?> GetOrLoadGroupDataAsync(string token, Guid groupId, Func<Task<GroupData?>> loader)
        => GetOrLoadGroupDataAsync(token, groupId, _ => loader());

    public bool TryGetGroupData(string token, Guid groupId, out GroupData? value)
    {
        if (!_tokens.Validate(token)) { value = default; return false; }
        return _cache.TryGet(GroupKey(token, groupId), out value);
    }

    public void SetGroupData(string token, Guid groupId, GroupData value)
    {
        if (!_tokens.Validate(token)) return;
        _cache.Set(GroupKey(token, groupId), value, EntryOptions(token, _groupTtl));
    }

    public void InvalidateGroup(string token, Guid groupId)
    {
        _cache.Remove(GroupKey(token, groupId));
    }

    public void InvalidateToken(string token)
    {
        var k = TokenKey.HashToken(token);
        if (_tokenCts.TryRemove(k, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
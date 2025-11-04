using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace PasswordManagerLocalBackend.Security;

public sealed class SafeMemoryCache
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inflight = new(StringComparer.Ordinal);

    public SafeMemoryCache(IMemoryCache cache) => _cache = cache;

    public bool TryGet<T>(string key, out T? value) => _cache.TryGetValue(key, out value);

    public void Set<T>(string key, T value, MemoryCacheEntryOptions options) => _cache.Set(key, value, options);

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, MemoryCacheEntryOptions options)
    {
        if (_cache.TryGetValue(key, out T? v)) return v;
        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<object?>>(async () =>
        {
            var result = await factory();
            if (result is not null) _cache.Set(key, result, options);
            return result;
        }));
        try
        {
            var obj = await (Task<object?>)lazy.Value;
            return (T?)obj;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    public void Remove(string key) => _cache.Remove(key);


}
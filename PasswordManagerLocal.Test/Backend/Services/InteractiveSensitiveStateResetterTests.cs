using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class InteractiveSensitiveStateResetterTests
{
    [TestMethod]
    public async Task ResetRevokesTokensDisposesKeysClearsCachesAndIsIdempotent()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var safeMemoryCache = new SafeMemoryCache(memoryCache);
        ITokenService tokens = new TokenService();
        IKeyVaultService keys = new KeyVaultService();
        IDataCachingService cache = new DataCachingService(safeMemoryCache, tokens);
        var resetter = new InteractiveSensitiveStateResetter(tokens, cache, keys, safeMemoryCache);
        var token = tokens.Issue(Guid.NewGuid());
        using var key = EncryptionKey.FromRaw(Enumerable.Repeat((byte)7, 32).ToArray());
        var userData = CreateUserData();

        keys.SetUserKey(token, key, DateTimeOffset.UtcNow.AddMinutes(5));
        cache.SetUserData(token, userData);
        Assert.IsTrue(tokens.Validate(token));
        Assert.IsTrue(keys.HasUserKey(token));
        Assert.IsTrue(cache.TryGetUserData(token, out _));

        await resetter.ResetAsync();
        await resetter.ResetAsync();

        Assert.IsFalse(tokens.Validate(token));
        Assert.IsFalse(keys.HasUserKey(token));
        Assert.IsFalse(cache.TryGetUserData(token, out _));
    }

    [TestMethod]
    public async Task CacheClearCancelsInflightPopulation()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var safeMemoryCache = new SafeMemoryCache(memoryCache);
        ITokenService tokens = new TokenService();
        IDataCachingService cache = new DataCachingService(safeMemoryCache, tokens);
        var token = tokens.Issue(Guid.NewGuid());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var loading = cache.GetOrLoadUserDataAsync(token, async cancellationToken =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CreateUserData();
        });

        await started.Task;
        cache.ClearAll();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await loading);
    }

    private static UserData CreateUserData()
    {
        var data = new UserData
        {
            UId = Guid.NewGuid(),
            GeneralUserDataKey = Enumerable.Repeat((byte)1, 32).ToArray(),
            GeneralUserDataIntegrityHash = Enumerable.Repeat((byte)2, 32).ToArray(),
            UserPasswordsDataKey = Enumerable.Repeat((byte)3, 32).ToArray(),
            UserPasswordsDataIntegrityHash = Enumerable.Repeat((byte)4, 32).ToArray(),
            UserDevicesDataKey = Enumerable.Repeat((byte)5, 32).ToArray(),
            UserDevicesDataIntegrityHash = Enumerable.Repeat((byte)6, 32).ToArray()
        };
        data.GenerateIntegrityHash();
        return data;
    }
}

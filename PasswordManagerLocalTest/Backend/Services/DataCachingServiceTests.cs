using global::PasswordManagerLocalTest.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models.Encrypted;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class DataCachingServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task InvalidateToken_ForcesReload()
    {
        using var host = new BackendTestHost();

        var tokens = (ITokenService)host.Services.GetRequiredService(typeof(ITokenService));
        var cache = (IDataCachingService)host.Services.GetRequiredService(typeof(IDataCachingService));

        var uid = Guid.NewGuid();
        var token = tokens.Issue(uid);

        var calls = 0;

        Task<UserData?> Loader()
        {
            calls++;

            var ud = new UserData
            {
                UId = Guid.NewGuid(),
                Username = "u",
                FirstName = "f",
                LastName = "l",
                Email = "e@e.com",
                RegistrationDate = DateTime.UtcNow,
                LastLoginDate = DateTime.UtcNow
            };
            ud.GenerateIntegrityHash();

            return Task.FromResult<UserData?>(ud);
        }

        var a = await cache.GetOrLoadUserDataAsync(token, Loader);
        var b = await cache.GetOrLoadUserDataAsync(token, Loader);

        MSTestAssert.IsNotNull(a);
        MSTestAssert.IsNotNull(b);
        MSTestAssert.AreEqual(1, calls);

        cache.InvalidateToken(token);

        var c = await cache.GetOrLoadUserDataAsync(token, Loader);

        MSTestAssert.IsNotNull(c);
        MSTestAssert.AreEqual(2, calls);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void InvalidateGroup_RemovesGroupDataFromCache()
    {
        using var host = new BackendTestHost();

        var tokens = (ITokenService)host.Services.GetRequiredService(typeof(ITokenService));
        var cache = (IDataCachingService)host.Services.GetRequiredService(typeof(IDataCachingService));

        var token = tokens.Issue(Guid.NewGuid());
        var groupId = Guid.NewGuid();

        var gd = new GroupData
        {
            Id = groupId,
            Name = "g",
            Description = "desc",
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
        gd.Passwords.GenerateIntegrityHash();
        gd.GenerateIntegrityHash();

        cache.SetGroupData(token, groupId, gd);

        MSTestAssert.IsTrue(cache.TryGetGroupData(token, groupId, out var before));
        MSTestAssert.AreSame(gd, before);

        cache.InvalidateGroup(token, groupId);

        MSTestAssert.IsFalse(cache.TryGetGroupData(token, groupId, out var after));
        MSTestAssert.IsNull(after);

        MSTestAssert.AreEqual(groupId, gd.Id);
        MSTestAssert.AreEqual("g", gd.Name);
    }
}
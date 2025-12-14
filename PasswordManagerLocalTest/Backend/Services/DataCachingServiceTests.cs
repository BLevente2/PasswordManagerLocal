using global::PasswordManagerLocalTest.TestInfrastructure;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models.Encrypted;

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

        Assert.IsNotNull(a);
        Assert.IsNotNull(b);
        Assert.AreEqual(1, calls);

        cache.InvalidateToken(token);

        var c = await cache.GetOrLoadUserDataAsync(token, Loader);

        Assert.IsNotNull(c);
        Assert.AreEqual(2, calls);
    }
}
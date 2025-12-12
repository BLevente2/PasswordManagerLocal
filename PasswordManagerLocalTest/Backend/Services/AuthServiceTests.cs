using global::PasswordManagerLocalTest.TestInfrastructure;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class AuthServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task Register_ThenLogin_Roundtrip_Works()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));
        var cache = (IDataCachingService)host.Services.GetRequiredService(typeof(IDataCachingService));
        var keys = (IKeyVaultService)host.Services.GetRequiredService(typeof(IKeyVaultService));

        var reg = host.CreateValidRegistrationRequest("alice");
        var token1 = await auth.RegisterAsync(reg);

        Assert.IsTrue(keys.HasUserKey(token1));
        Assert.IsTrue(cache.TryGetUserData(token1, out var ud1));
        Assert.IsNotNull(ud1);
        Assert.AreEqual("alice", ud1.Username);

        var login = host.CreateValidLoginRequest("alice");
        var token2 = await auth.LoginAsync(login);

        Assert.IsTrue(keys.HasUserKey(token2));
        Assert.IsTrue(cache.TryGetUserData(token2, out var ud2));
        Assert.IsNotNull(ud2);
        Assert.AreEqual("alice", ud2.Username);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task Register_DuplicateUsername_Throws()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));

        await auth.RegisterAsync(host.CreateValidRegistrationRequest("bob"));

        await Assert.ThrowsExceptionAsync<InvalidInputException>(async () =>
        {
            await auth.RegisterAsync(host.CreateValidRegistrationRequest("bob"));
        });
    }
}

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
        var tokens = (ITokenService)host.Services.GetRequiredService(typeof(ITokenService));

        var reg = host.CreateValidRegistrationRequest("alice");
        var token1 = await auth.RegisterAsync(reg);

        Assert.AreNotEqual(Guid.Empty, token1);
        Assert.IsTrue(tokens.Validate(token1));
        Assert.IsTrue(keys.HasUserKey(token1));
        Assert.IsTrue(cache.TryGetUserData(token1, out var ud1));
        Assert.IsNotNull(ud1);
        Assert.AreEqual("alice", ud1.Username);

        var login = host.CreateValidLoginRequest("alice");
        var token2 = await auth.LoginAsync(login);

        Assert.AreNotEqual(Guid.Empty, token2);
        Assert.IsTrue(tokens.Validate(token2));
        Assert.IsTrue(keys.HasUserKey(token2));
        Assert.IsTrue(cache.TryGetUserData(token2, out var ud2));
        Assert.IsNotNull(ud2);
        Assert.AreEqual("alice", ud2.Username);

        Assert.AreNotEqual(token1, token2);
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

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task Logout_ValidToken_InvalidatesEverything()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));
        var cache = (IDataCachingService)host.Services.GetRequiredService(typeof(IDataCachingService));
        var keys = (IKeyVaultService)host.Services.GetRequiredService(typeof(IKeyVaultService));
        var tokens = (ITokenService)host.Services.GetRequiredService(typeof(ITokenService));

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("charlie"));

        Assert.IsTrue(tokens.Validate(token));
        Assert.IsTrue(keys.HasUserKey(token));
        Assert.IsTrue(cache.TryGetUserData(token, out _));

        auth.Logout(token);

        Assert.IsFalse(tokens.Validate(token));
        Assert.IsFalse(keys.HasUserKey(token));
        Assert.IsFalse(cache.TryGetUserData(token, out _));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void Logout_InvalidToken_Throws()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));

        var invalidToken = Guid.NewGuid();

        Assert.ThrowsException<InvalidTokenException>(() =>
        {
            auth.Logout(invalidToken);
        });
    }
}
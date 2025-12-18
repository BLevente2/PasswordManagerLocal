using global::PasswordManagerLocalTest.TestInfrastructure;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Requests;
using System.Text;

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

        Assert.ThrowsException<InvalidTokenException>(() =>
        {
            auth.Logout(Guid.NewGuid());
        });
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task ChangeMasterPassword_ValidRequest_Works()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("dave"));

        var request = new MasterPasswordChangeRequest
        {
            Token = token,
            Password = Encoding.UTF8.GetBytes("P@ssw0rd12345678"),
            NewPassword = Encoding.UTF8.GetBytes("N3wP@ssw0rd_123456")
        };

        await auth.ChangeMasterPasswordAsync(request);

        var login = new LoginRequest
        {
            Username = "dave",
            Password = Encoding.UTF8.GetBytes("N3wP@ssw0rd_123456"),
            RememberMe = false
        };

        var newToken = await auth.LoginAsync(login);

        Assert.AreNotEqual(Guid.Empty, newToken);
        Assert.AreNotEqual(token, newToken);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task ChangeMasterPassword_WrongCurrentPassword_Throws()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("eve"));

        var request = new MasterPasswordChangeRequest
        {
            Token = token,
            Password = Encoding.UTF8.GetBytes("WRONG_PASSWORD"),
            NewPassword = Encoding.UTF8.GetBytes("AnotherValidPassword123")
        };

        await Assert.ThrowsExceptionAsync<InvalidInputException>(async () =>
        {
            await auth.ChangeMasterPasswordAsync(request);
        });
    }
}
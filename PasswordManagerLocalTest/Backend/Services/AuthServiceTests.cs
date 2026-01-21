using global::PasswordManagerLocalTest.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Requests;
using System.Text;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

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

        MSTestAssert.AreNotEqual(Guid.Empty, token1);
        MSTestAssert.IsTrue(tokens.Validate(token1));
        MSTestAssert.IsTrue(keys.HasUserKey(token1));
        MSTestAssert.IsTrue(cache.TryGetUserData(token1, out var ud1));
        MSTestAssert.IsNotNull(ud1);
        MSTestAssert.AreEqual("alice", ud1.Username);

        var login = host.CreateValidLoginRequest("alice");
        var token2 = await auth.LoginAsync(login);

        MSTestAssert.AreNotEqual(Guid.Empty, token2);
        MSTestAssert.IsTrue(tokens.Validate(token2));
        MSTestAssert.IsTrue(keys.HasUserKey(token2));
        MSTestAssert.IsTrue(cache.TryGetUserData(token2, out var ud2));
        MSTestAssert.IsNotNull(ud2);
        MSTestAssert.AreEqual("alice", ud2.Username);

        MSTestAssert.AreNotEqual(token1, token2);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task Register_DuplicateUsername_Throws()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));

        await auth.RegisterAsync(host.CreateValidRegistrationRequest("bob"));

        await ExpectThrowsAsync<InvalidInputException>(async () =>
        {
            await auth.RegisterAsync(host.CreateValidRegistrationRequest("bob"));
        });
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void Logout_InvalidToken_Throws()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));

        ExpectThrows<InvalidTokenException>(() =>
        {
            auth.Logout(Guid.NewGuid());
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

        MSTestAssert.IsTrue(tokens.Validate(token));
        MSTestAssert.IsTrue(keys.HasUserKey(token));
        MSTestAssert.IsTrue(cache.TryGetUserData(token, out _));

        auth.Logout(token);

        MSTestAssert.IsFalse(tokens.Validate(token));
        MSTestAssert.IsFalse(keys.HasUserKey(token));
        MSTestAssert.IsFalse(cache.TryGetUserData(token, out _));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task ChangeMasterPassword_ValidRequest_Works()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("dave"));

        var newPassword = Encoding.UTF8.GetBytes("N3wP@ssw0rd_123456");

        var request = new MasterPasswordChangeRequest
        {
            Token = token,
            Password = Encoding.UTF8.GetBytes("P@ssw0rd12345678"),
            NewPassword = newPassword
        };

        await auth.ChangeMasterPasswordAsync(request);

        var login = new LoginRequest
        {
            Username = "dave",
            Password = newPassword,
            RememberMe = false
        };

        var newToken = await auth.LoginAsync(login);

        MSTestAssert.AreNotEqual(Guid.Empty, newToken);
        MSTestAssert.AreNotEqual(token, newToken);
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

        await ExpectThrowsAsync<InvalidInputException>(async () =>
        {
            await auth.ChangeMasterPasswordAsync(request);
        });
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task Register_RememberMeEnabled_SavesKey()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));
        var users = (IUserService)host.Services.GetRequiredService(typeof(IUserService));

        var reg = host.CreateValidRegistrationRequest("remember_me_user");
        reg.RememberMe = true;

        await auth.RegisterAsync(reg);

        var user = await users.GetUserByUsernameAsync(Encoding.UTF8.GetBytes("remember_me_user"));
        MSTestAssert.IsNotNull(user);
        MSTestAssert.IsNotNull(user.SavedKey);
        MSTestAssert.IsNotEmpty(user.SavedKey);
    }

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            MSTestAssert.Fail($"Expected exception: {typeof(TException).Name}");
        }
        catch (TException)
        {
        }
    }

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
            MSTestAssert.Fail($"Expected exception: {typeof(TException).Name}");
        }
        catch (TException)
        {
        }
    }
}
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
public sealed class UserProfileServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task GetUserProfileInfo_AfterRegister_ReturnsExpected()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var profile = host.Services.GetRequiredService<IUserProfileService>();

        var reg = host.CreateValidRegistrationRequest("alice");
        reg.FirstName = "Alice";
        reg.LastName = "Liddell";
        reg.Email = "alice@example.com";

        var token = await auth.RegisterAsync(reg);

        var info = await profile.GetUserProfileInfoAsync(token);

        MSTestAssert.AreEqual("alice", info.Username);
        MSTestAssert.AreEqual("Alice", info.FirstName);
        MSTestAssert.AreEqual("Liddell", info.LastName);
        MSTestAssert.AreEqual("alice@example.com", info.Email);
        MSTestAssert.IsTrue(info.RegistrationDate > DateTime.MinValue);
        MSTestAssert.IsTrue(info.LastLoginDate > DateTime.MinValue);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task UpdateUserProfileInfo_ChangesPersist()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var profile = host.Services.GetRequiredService<IUserProfileService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("bob"));

        await profile.UpdateUserProfileInfoAsync(new UpdateUserProfileRequest
        {
            Token = token,
            NewEamil = "bob.new@example.com",
            newFirstName = "Bobby",
            NewLastName = "Tables"
        });

        var info = await profile.GetUserProfileInfoAsync(token);

        MSTestAssert.AreEqual("bob", info.Username);
        MSTestAssert.AreEqual("Bobby", info.FirstName);
        MSTestAssert.AreEqual("Tables", info.LastName);
        MSTestAssert.AreEqual("bob.new@example.com", info.Email);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task ChangeUsername_OldLoginFails_NewLoginWorks_AndProfileUpdated()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var profile = host.Services.GetRequiredService<IUserProfileService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("carol"));

        await profile.ChangeUsernameAsync(token, "carol2");

        var info = await profile.GetUserProfileInfoAsync(token);
        MSTestAssert.AreEqual("carol2", info.Username);

        await ExpectThrowsAsync<UserNotFoundException>(async () =>
        {
            await auth.LoginAsync(host.CreateValidLoginRequest("carol"));
        });

        var token2 = await auth.LoginAsync(host.CreateValidLoginRequest("carol2"));
        MSTestAssert.AreNotEqual(Guid.Empty, token2);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task DeleteUserAccount_CorrectPassword_DeletesUser_AndInvalidatesToken()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var profile = host.Services.GetRequiredService<IUserProfileService>();
        var users = host.Services.GetRequiredService<IUserService>();
        var tokens = host.Services.GetRequiredService<ITokenService>();
        var keys = host.Services.GetRequiredService<IKeyVaultService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("dave"));
        var uid = users.GetUidFromToken(token);

        await profile.DeleteUserAccountAsync(token, Encoding.UTF8.GetBytes("P@ssw0rd12345678"));

        var exists = await users.UserExistsAsync(uid);
        MSTestAssert.IsFalse(exists);

        MSTestAssert.IsFalse(tokens.Validate(token));
        MSTestAssert.IsFalse(keys.HasUserKey(token));

        await ExpectThrowsAsync<InvalidTokenException>(async () =>
        {
            await profile.GetUserProfileInfoAsync(token);
        });
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task InvalidRequests_Throw()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var profile = host.Services.GetRequiredService<IUserProfileService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("erin"));

        await ExpectThrowsAsync<InvalidInputException>(async () =>
        {
            await profile.UpdateUserProfileInfoAsync(new UpdateUserProfileRequest
            {
                Token = token
            });
        });

        await ExpectThrowsAsync<InvalidInputException>(async () =>
        {
            await profile.ChangeUsernameAsync(token, "");
        });

        await ExpectThrowsAsync<InvalidTokenException>(async () =>
        {
            await profile.GetUserProfileInfoAsync(Guid.NewGuid());
        });
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
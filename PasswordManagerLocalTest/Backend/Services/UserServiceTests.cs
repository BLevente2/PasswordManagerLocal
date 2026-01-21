using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalTest.TestInfrastructure;
using System.Text;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class UserServiceTests
{
    [TestMethod]
    public async Task GetAndVerifyUserByUid_ExistingUser_Works()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var users = host.Services.GetRequiredService<IUserService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("alice"));
        var uid = users.GetUidFromToken(token);

        var fetched = await users.GetAndVerifyUserByUidAsync(uid);

        MSTestAssert.AreEqual(uid, fetched.UId);
    }

    [TestMethod]
    public async Task GetAndVerifyUserByUid_NonExisting_Throws()
    {
        using var host = new BackendTestHost();
        var users = host.Services.GetRequiredService<IUserService>();

        await ExpectThrowsAsync<UserNotFoundException>(async () =>
        {
            await users.GetAndVerifyUserByUidAsync(Guid.NewGuid());
        });
    }

    [TestMethod]
    public async Task GetUserByUsername_Existing_Works()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var users = host.Services.GetRequiredService<IUserService>();

        await auth.RegisterAsync(host.CreateValidRegistrationRequest("bob"));

        var user = await users.GetAndVerifyUserByUsernameAsync(Encoding.UTF8.GetBytes("bob"));

        MSTestAssert.IsNotNull(user);
    }

    [TestMethod]
    public async Task GetUserByUsername_NonExisting_ReturnsNull()
    {
        using var host = new BackendTestHost();
        var users = host.Services.GetRequiredService<IUserService>();

        var user = await users.GetUserByUsernameAsync(Encoding.UTF8.GetBytes("nobody"));

        MSTestAssert.IsNull(user);
    }

    [TestMethod]
    public async Task GetLoadAndVerifyUserData_FirstLoad_ThenCached()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var users = host.Services.GetRequiredService<IUserService>();
        var cache = host.Services.GetRequiredService<IDataCachingService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("charlie"));

        var data1 = await users.GetLoadAndVerifyUserDataAsync(token);
        var data2 = await users.GetLoadAndVerifyUserDataAsync(token);

        MSTestAssert.IsNotNull(data1);
        MSTestAssert.AreSame(data1, data2);
        MSTestAssert.IsTrue(cache.TryGetUserData(token, out _));
    }

    [TestMethod]
    public async Task GetLoadAndVerifyUserData_InvalidToken_Throws()
    {
        using var host = new BackendTestHost();
        var users = host.Services.GetRequiredService<IUserService>();

        await ExpectThrowsAsync<InvalidTokenException>(async () =>
        {
            await users.GetLoadAndVerifyUserDataAsync(Guid.NewGuid());
        });
    }

    [TestMethod]
    public async Task UpdateUserData_ChangesPersist()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var users = host.Services.GetRequiredService<IUserService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("dave"));

        var data = await users.GetLoadAndVerifyUserDataAsync(token);
        data.FirstName = "Updated";

        await users.UpdateUserDataAsync(data, token);

        var reloaded = await users.GetLoadAndVerifyUserDataAsync(token);

        MSTestAssert.AreEqual("Updated", reloaded.FirstName);
    }

    [TestMethod]
    public async Task DeleteUserByToken_RemovesUser()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var users = host.Services.GetRequiredService<IUserService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("eve"));
        var uid = users.GetUidFromToken(token);

        await users.DeleteUserByTokenAsync(token);

        var exists = await users.UserExistsAsync(uid);

        MSTestAssert.IsFalse(exists);
    }

    [TestMethod]
    public async Task GetAndVerifyRememberMeEnabledUsers_ReturnsOnlyEnabled()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var users = host.Services.GetRequiredService<IUserService>();

        await auth.RegisterAsync(host.CreateValidRegistrationRequest("steve"));

        var rmEnabledUser = host.CreateValidRegistrationRequest("alice");
        rmEnabledUser.RememberMe = true;
        await auth.RegisterAsync(rmEnabledUser);

        var list = await users.GetAndVerifyRememberMeEnabledUsersAsync();

        MSTestAssert.HasCount(1, list);
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
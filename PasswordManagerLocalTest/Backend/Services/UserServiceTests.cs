using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalTest.TestInfrastructure;
using System.Text;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class UserServiceTests
{
    [TestMethod]
    public async Task GetAndVerifyUserByUid_ExistingUser_Works()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));
        var users = (IUserService)host.Services.GetRequiredService(typeof(IUserService));

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("alice"));
        var user = await users.GetUserByTokenAsync(token);

        var fetched = await users.GetAndVerifyUserByUidAsync(user.UId);

        Assert.IsNotNull(fetched);
        Assert.AreEqual(user.UId, fetched.UId);
    }

    [TestMethod]
    public async Task GetAndVerifyUserByUid_NonExisting_Throws()
    {
        using var host = new BackendTestHost();
        var users = (IUserService)host.Services.GetRequiredService(typeof(IUserService));

        await Assert.ThrowsExceptionAsync<UserNotFoundException>(async () =>
        {
            await users.GetAndVerifyUserByUidAsync(Guid.NewGuid());
        });
    }

    [TestMethod]
    public async Task GetUserData_ByToken_WorksAndCached()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));
        var users = (IUserService)host.Services.GetRequiredService(typeof(IUserService));
        var cache = (IDataCachingService)host.Services.GetRequiredService(typeof(IDataCachingService));

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("bob"));

        var data = await users.GetUserDataAsync(token);

        Assert.IsNotNull(data);
        Assert.AreEqual("bob", data.Username);
        Assert.IsTrue(cache.TryGetUserData(token, out _));
    }

    [TestMethod]
    public async Task GetUserData_InvalidToken_Throws()
    {
        using var host = new BackendTestHost();
        var users = (IUserService)host.Services.GetRequiredService(typeof(IUserService));

        await Assert.ThrowsExceptionAsync<InvalidTokenException>(async () =>
        {
            await users.GetUserDataAsync(Guid.NewGuid());
        });
    }

    [TestMethod]
    public async Task GetUserByUsername_Existing_Works()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));
        var users = (IUserService)host.Services.GetRequiredService(typeof(IUserService));

        await auth.RegisterAsync(host.CreateValidRegistrationRequest("charlie"));

        var user = await users.GetAndVerifyUserByUsernameAsync(Encoding.UTF8.GetBytes("charlie"));

        Assert.IsNotNull(user);
    }

    [TestMethod]
    public async Task GetUserByUsername_NonExisting_ReturnsNull()
    {
        using var host = new BackendTestHost();
        var users = (IUserService)host.Services.GetRequiredService(typeof(IUserService));

        var user = await users.GetUserByUsernameAsync(Encoding.UTF8.GetBytes("nobody"));

        Assert.IsNull(user);
    }

    [TestMethod]
    public async Task UpdateAndSave_UserData_ChangesPersist()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));
        var users = (IUserService)host.Services.GetRequiredService(typeof(IUserService));

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("dave"));

        var data = await users.GetUserDataAsync(token);
        data.FirstName = "Updated";

        await users.UpdateAndSaveAsync(data, token: token);

        var reloaded = await users.GetUserDataAsync(token);
        Assert.AreEqual("Updated", reloaded.FirstName);
    }
}
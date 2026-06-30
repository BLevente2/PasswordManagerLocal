using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalTest.TestInfrastructure;
using System.Text;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class UserPasswordsServiceTests
{
    [TestMethod]
    public async Task AddNewPassword_ThenList_Works()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var svc = host.Services.GetRequiredService<IUserPasswordsService>();
        var cache = host.Services.GetRequiredService<IDataCachingService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("steve"));

        await svc.AddNewPasswordAsync(token, new NewPasswordRequest
        {
            Name = "Email",
            Password = Encoding.UTF8.GetBytes("secret")
        });

        cache.InvalidateToken(token);
        var response = await svc.GetSavedPasswordsAsync(token);

        MSTestAssert.HasCount(1, response.Passwords);
        MSTestAssert.IsEmpty(response.CustomColors);
        MSTestAssert.AreEqual("Email", response.Passwords[0].Name);
    }


    [TestMethod]
    public async Task AddNewPassword_DuplicateName_Throws()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));
        var svc = (IUserPasswordsService)host.Services.GetRequiredService(typeof(IUserPasswordsService));

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("duplicate"));

        await svc.AddNewPasswordAsync(token, new NewPasswordRequest
        {
            Name = "Email",
            Password = Encoding.UTF8.GetBytes("first")
        });

        await ExpectThrowsAsync<DuplicatePasswordNameException>(async () =>
        {
            await svc.AddNewPasswordAsync(token, new NewPasswordRequest
            {
                Name = "email",
                Password = Encoding.UTF8.GetBytes("second")
            });
        });

        var response = await svc.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(1, response.Passwords);
    }

    [TestMethod]
    public async Task RemovePassword_RemovesPersistently()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var svc = host.Services.GetRequiredService<IUserPasswordsService>();
        var cache = host.Services.GetRequiredService<IDataCachingService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("bob"));

        await svc.AddNewPasswordAsync(token, new NewPasswordRequest
        {
            Name = "Test",
            Password = Encoding.UTF8.GetBytes("pw")
        });

        var response = await svc.GetSavedPasswordsAsync(token);
        var id = response.Passwords[0].Id;

        await svc.RemovePasswordAsync(token, id);

        cache.InvalidateToken(token);
        var after = await svc.GetSavedPasswordsAsync(token);
        MSTestAssert.IsEmpty(after.Passwords);
    }

    [TestMethod]
    public async Task GetUnsecurePassword_Roundtrip_Works()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));
        var svc = (IUserPasswordsService)host.Services.GetRequiredService(typeof(IUserPasswordsService));

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("charlie"));

        var raw = Encoding.UTF8.GetBytes("topsecret");

        await svc.AddNewPasswordAsync(token, new NewPasswordRequest
        {
            Name = "Vault",
            Password = raw
        });

        var response = await svc.GetSavedPasswordsAsync(token);
        var decrypted = await svc.GetUnsecurePasswordAsync(token, response.Passwords[0].Id);

        CollectionAssert.AreEqual(raw, decrypted);
    }

    [TestMethod]
    public async Task UpdatePassword_PersistsChanges()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var svc = host.Services.GetRequiredService<IUserPasswordsService>();
        var cache = host.Services.GetRequiredService<IDataCachingService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("dave"));

        await svc.AddNewPasswordAsync(token, new NewPasswordRequest
        {
            Name = "Old",
            Password = Encoding.UTF8.GetBytes("oldpw")
        });

        var response = await svc.GetSavedPasswordsAsync(token);
        var id = response.Passwords[0].Id;

        await svc.UpdatePasswordAsync(token, new UpdatePasswordRequest
        {
            Id = id,
            Name = "New",
            Password = Encoding.UTF8.GetBytes("newpw")
        });

        cache.InvalidateToken(token);
        var updated = await svc.GetSavedPasswordsAsync(token);
        var decrypted = await svc.GetUnsecurePasswordAsync(token, id);

        MSTestAssert.HasCount(1, updated.Passwords);
        MSTestAssert.AreEqual("New", updated.Passwords[0].Name);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("newpw"), decrypted);
    }


    [TestMethod]
    public async Task CustomUserColors_AreIncludedInSavedPasswordsResponse()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var passwordService = host.Services.GetRequiredService<IUserPasswordsService>();
        var customColorService = host.Services.GetRequiredService<IUserCustomColorService>();
        var cache = host.Services.GetRequiredService<IDataCachingService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("colors"));

        await customColorService.AddCustomUserColorAsync(token, new NewCustomUserColorRequest
        {
            ColorName = "Work",
            ColorCode = "#FF123456"
        });

        cache.InvalidateToken(token);
        var response = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.IsEmpty(response.Passwords);
        MSTestAssert.HasCount(1, response.CustomColors);
        MSTestAssert.AreEqual("Work", response.CustomColors[0].ColorName);
        MSTestAssert.AreEqual("#FF123456", response.CustomColors[0].ColorCode);
    }


    [TestMethod]
    public async Task InvalidToken_Throws()
    {
        using var host = new BackendTestHost();
        var svc = (IUserPasswordsService)host.Services.GetRequiredService(typeof(IUserPasswordsService));

        await ExpectThrowsAsync<InvalidTokenException>(async () =>
        {
            await svc.GetSavedPasswordsAsync(Guid.NewGuid());
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
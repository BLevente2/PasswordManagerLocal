using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Abstractions;
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
    public async Task ExportPasswordsToUser_CopiesSelectedPasswordsAndKeepsSourcePassword()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var svc = host.Services.GetRequiredService<IUserPasswordsService>();
        var cache = host.Services.GetRequiredService<IDataCachingService>();

        var sourceToken = await auth.RegisterAsync(host.CreateValidRegistrationRequest("export-source"));
        var targetToken = await auth.RegisterAsync(host.CreateValidRegistrationRequest("export-target"));

        var emailPassword = Encoding.UTF8.GetBytes("email-secret");
        var bankPassword = Encoding.UTF8.GetBytes("bank-secret");

        await svc.AddNewPasswordAsync(sourceToken, new NewPasswordRequest
        {
            Name = "Email",
            Description = "Primary email",
            Color = "#FF123456",
            Password = emailPassword
        });

        await svc.AddNewPasswordAsync(sourceToken, new NewPasswordRequest
        {
            Name = "Bank",
            Description = "Bank login",
            Color = "#FF654321",
            Password = bankPassword
        });

        var sourceBefore = await svc.GetSavedPasswordsAsync(sourceToken);
        var exportedIds = sourceBefore.Passwords
            .OrderBy(password => password.Name, StringComparer.OrdinalIgnoreCase)
            .Select(password => password.Id)
            .ToList();

        await svc.ExportPasswordsToUserAsync(sourceToken, new ExportPasswordsToUserRequest
        {
            TargetToken = targetToken,
            PasswordIds = exportedIds
        });

        cache.InvalidateToken(sourceToken);
        cache.InvalidateToken(targetToken);

        var sourceAfter = await svc.GetSavedPasswordsAsync(sourceToken);
        var targetAfter = await svc.GetSavedPasswordsAsync(targetToken);

        MSTestAssert.HasCount(2, sourceAfter.Passwords);
        MSTestAssert.HasCount(2, targetAfter.Passwords);

        var targetEmail = targetAfter.Passwords.Single(password => password.Name == "Email");
        var targetBank = targetAfter.Passwords.Single(password => password.Name == "Bank");

        MSTestAssert.AreEqual("Primary email", targetEmail.Description);
        MSTestAssert.AreEqual("#FF123456", targetEmail.Color);
        CollectionAssert.AreEqual(emailPassword, await svc.GetUnsecurePasswordAsync(targetToken, targetEmail.Id));
        CollectionAssert.AreEqual(bankPassword, await svc.GetUnsecurePasswordAsync(targetToken, targetBank.Id));

        var sourceEmail = sourceAfter.Passwords.Single(password => password.Name == "Email");
        CollectionAssert.AreEqual(emailPassword, await svc.GetUnsecurePasswordAsync(sourceToken, sourceEmail.Id));
    }


    [TestMethod]
    public async Task ExportPasswordsToUser_Endpoint_WiresToService()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var endpoints = host.Services.GetRequiredService<IEndpoints>();

        var sourceToken = await auth.RegisterAsync(host.CreateValidRegistrationRequest("export-endpoint-source"));
        var targetToken = await auth.RegisterAsync(host.CreateValidRegistrationRequest("export-endpoint-target"));

        await endpoints.AddNewPasswordAsync(sourceToken, new NewPasswordRequest
        {
            Name = "Forum",
            Password = Encoding.UTF8.GetBytes("forum-secret")
        });

        var sourcePasswords = await endpoints.GetSavedPasswordsAsync(sourceToken);

        await endpoints.ExportPasswordsToUserAsync(sourceToken, new ExportPasswordsToUserRequest
        {
            TargetToken = targetToken,
            PasswordIds = [sourcePasswords.Passwords[0].Id]
        });

        var targetPasswords = await endpoints.GetSavedPasswordsAsync(targetToken);
        MSTestAssert.HasCount(1, targetPasswords.Passwords);
        MSTestAssert.AreEqual("Forum", targetPasswords.Passwords[0].Name);
        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("forum-secret"),
            await endpoints.GetUnsecurePasswordAsync(targetToken, targetPasswords.Passwords[0].Id));
    }


    [TestMethod]
    public async Task ExportPasswordsToUser_DuplicateTargetName_ThrowsAndDoesNotModifyTarget()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var svc = host.Services.GetRequiredService<IUserPasswordsService>();

        var sourceToken = await auth.RegisterAsync(host.CreateValidRegistrationRequest("export-duplicate-source"));
        var targetToken = await auth.RegisterAsync(host.CreateValidRegistrationRequest("export-duplicate-target"));

        await svc.AddNewPasswordAsync(sourceToken, new NewPasswordRequest
        {
            Name = "Email",
            Password = Encoding.UTF8.GetBytes("source-secret")
        });

        await svc.AddNewPasswordAsync(targetToken, new NewPasswordRequest
        {
            Name = "email",
            Password = Encoding.UTF8.GetBytes("target-secret")
        });

        var sourcePasswords = await svc.GetSavedPasswordsAsync(sourceToken);

        await ExpectThrowsAsync<DuplicatePasswordNameException>(async () =>
        {
            await svc.ExportPasswordsToUserAsync(sourceToken, new ExportPasswordsToUserRequest
            {
                TargetToken = targetToken,
                PasswordIds = [sourcePasswords.Passwords[0].Id]
            });
        });

        var targetPasswords = await svc.GetSavedPasswordsAsync(targetToken);
        MSTestAssert.HasCount(1, targetPasswords.Passwords);
        MSTestAssert.AreEqual("email", targetPasswords.Passwords[0].Name);
        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("target-secret"),
            await svc.GetUnsecurePasswordAsync(targetToken, targetPasswords.Passwords[0].Id));
    }


    [TestMethod]
    public async Task ExportPasswordsToUser_InvalidRequest_Throws()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var svc = host.Services.GetRequiredService<IUserPasswordsService>();

        var sourceToken = await auth.RegisterAsync(host.CreateValidRegistrationRequest("export-invalid-source"));
        var targetToken = await auth.RegisterAsync(host.CreateValidRegistrationRequest("export-invalid-target"));

        await ExpectThrowsAsync<InvalidInputException>(async () =>
        {
            await svc.ExportPasswordsToUserAsync(sourceToken, new ExportPasswordsToUserRequest
            {
                TargetToken = targetToken,
                PasswordIds = []
            });
        });
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
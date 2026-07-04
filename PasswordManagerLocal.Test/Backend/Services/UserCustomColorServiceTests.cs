using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Test.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class UserCustomColorServiceTests
{
    [TestMethod]
    public async Task AddCustomUserColors_AddsEveryColorInSingleBatch()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var customColorService = host.Services.GetRequiredService<IUserCustomColorService>();
        var passwordService = host.Services.GetRequiredService<IUserPasswordsService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("colors-batch"));

        await customColorService.AddCustomUserColorsAsync(token,
        [
            new NewCustomUserColorRequest { ColorName = "Blue", ColorCode = "#FF010203" },
            new NewCustomUserColorRequest { ColorName = "Green", ColorCode = "#FF040506" },
            new NewCustomUserColorRequest { ColorName = "Red", ColorCode = "#FF070809" }
        ]);

        var response = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(3, response.CustomColors);
        CollectionAssert.AreEquivalent(
            new[] { "Blue", "Green", "Red" },
            response.CustomColors.Select(color => color.ColorName).ToArray());
    }


    [TestMethod]
    public async Task AddCustomUserColors_DuplicateWithinBatch_ThrowsWithoutAddingAnyColor()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var customColorService = host.Services.GetRequiredService<IUserCustomColorService>();
        var passwordService = host.Services.GetRequiredService<IUserPasswordsService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("colors-batch-atomic"));

        await ExpectThrowsAsync<DuplicateCustomUserColorNameException>(async () =>
        {
            await customColorService.AddCustomUserColorsAsync(token,
            [
                new NewCustomUserColorRequest { ColorName = "Duplicate", ColorCode = "#FF010203" },
                new NewCustomUserColorRequest { ColorName = " duplicate ", ColorCode = "#FF040506" }
            ]);
        });

        var response = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.IsEmpty(response.CustomColors);
    }



    [TestMethod]
    public async Task AddUpdateDeleteCustomUserColor_PersistsThroughEncryptedUserPasswordsBlob()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var customColorService = host.Services.GetRequiredService<IUserCustomColorService>();
        var passwordService = host.Services.GetRequiredService<IUserPasswordsService>();
        var cache = host.Services.GetRequiredService<IDataCachingService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("colors-crud"));

        await AddColorAsync(customColorService, token, new NewCustomUserColorRequest
        {
            ColorName = "Work",
            ColorCode = "#ff123456"
        });

        cache.InvalidateToken(token);
        var added = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(1, added.CustomColors);
        MSTestAssert.AreEqual("Work", added.CustomColors[0].ColorName);
        MSTestAssert.AreEqual("#FF123456", added.CustomColors[0].ColorCode);

        var colorId = added.CustomColors[0].Id;
        await customColorService.UpdateCustomUserColorAsync(token, new UpdateCustomUserColorRequest
        {
            Id = colorId,
            ClearColorName = true,
            ColorCode = "#FF654321"
        });

        cache.InvalidateToken(token);
        var updated = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(1, updated.CustomColors);
        MSTestAssert.IsNull(updated.CustomColors[0].ColorName);
        MSTestAssert.AreEqual("#FF654321", updated.CustomColors[0].ColorCode);

        await customColorService.DeleteCustomUserColorAsync(token, colorId);

        cache.InvalidateToken(token);
        var deleted = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.IsEmpty(deleted.CustomColors);
    }


    [TestMethod]
    public async Task AddCustomUserColor_DuplicateCode_ThrowsAndKeepsExistingColor()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var customColorService = host.Services.GetRequiredService<IUserCustomColorService>();
        var passwordService = host.Services.GetRequiredService<IUserPasswordsService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("colors-duplicate"));

        await AddColorAsync(customColorService, token, new NewCustomUserColorRequest
        {
            ColorName = "Original",
            ColorCode = "#FF010203"
        });

        await ExpectThrowsAsync<DuplicateCustomUserColorCodeException>(async () =>
        {
            await AddColorAsync(customColorService, token, new NewCustomUserColorRequest
            {
                ColorName = "Duplicate",
                ColorCode = "#ff010203"
            });
        });

        var response = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(1, response.CustomColors);
        MSTestAssert.AreEqual("Original", response.CustomColors[0].ColorName);
    }


    [TestMethod]
    public async Task AddCustomUserColor_DuplicateName_ThrowsAndKeepsExistingColor()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var customColorService = host.Services.GetRequiredService<IUserCustomColorService>();
        var passwordService = host.Services.GetRequiredService<IUserPasswordsService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("colors-duplicate-name"));

        await AddColorAsync(customColorService, token, new NewCustomUserColorRequest
        {
            ColorName = "Original",
            ColorCode = "#FF010203"
        });

        await ExpectThrowsAsync<DuplicateCustomUserColorNameException>(async () =>
        {
            await AddColorAsync(customColorService, token, new NewCustomUserColorRequest
            {
                ColorName = "  original  ",
                ColorCode = "#FF010204"
            });
        });

        var response = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(1, response.CustomColors);
        MSTestAssert.AreEqual("Original", response.CustomColors[0].ColorName);
    }


    [TestMethod]
    public async Task UpdateCustomUserColor_InvalidRequest_Throws()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var customColorService = host.Services.GetRequiredService<IUserCustomColorService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("colors-invalid-update"));

        await ExpectThrowsAsync<InvalidInputException>(async () =>
        {
            await customColorService.UpdateCustomUserColorAsync(token, new UpdateCustomUserColorRequest
            {
                Id = Guid.NewGuid()
            });
        });
    }


    [TestMethod]
    public async Task DeleteCustomUserColor_NonExistingColor_Throws()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var customColorService = host.Services.GetRequiredService<IUserCustomColorService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("colors-missing-delete"));

        await ExpectThrowsAsync<CustomUserColorNotFoundException>(async () =>
        {
            await customColorService.DeleteCustomUserColorAsync(token, Guid.NewGuid());
        });
    }


    [TestMethod]
    public async Task InvalidToken_Throws()
    {
        using var host = new BackendTestHost();
        var customColorService = host.Services.GetRequiredService<IUserCustomColorService>();

        await ExpectThrowsAsync<InvalidTokenException>(async () =>
        {
            await AddColorAsync(customColorService, Guid.NewGuid(), new NewCustomUserColorRequest
            {
                ColorCode = "#FF000001"
            });
        });
    }


    private static Task AddColorAsync(
        IUserCustomColorService customColorService,
        Guid token,
        NewCustomUserColorRequest request) =>
        customColorService.AddCustomUserColorsAsync(token, [request]);


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

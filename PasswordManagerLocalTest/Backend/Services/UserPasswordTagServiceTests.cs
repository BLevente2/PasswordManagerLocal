using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalTest.TestInfrastructure;
using System.Text;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class UserPasswordTagServiceTests
{
    [TestMethod]
    public async Task AddUpdateDeletePasswordTag_PersistsThroughEncryptedUserPasswordsBlob()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var tagService = host.Services.GetRequiredService<IUserPasswordTagService>();
        var passwordService = host.Services.GetRequiredService<IUserPasswordsService>();
        var cache = host.Services.GetRequiredService<IDataCachingService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("tags-crud"));

        await tagService.AddPasswordTagAsync(token, new NewPasswordTagRequest
        {
            Name = "Work",
            Color = "#ff123456"
        });

        cache.InvalidateToken(token);
        var added = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(1, added.Tags);
        MSTestAssert.AreEqual("Work", added.Tags[0].Name);
        MSTestAssert.AreEqual("#FF123456", added.Tags[0].Color);

        var tagId = added.Tags[0].Id;
        await tagService.UpdatePasswordTagAsync(token, new UpdatePasswordTagRequest
        {
            Id = tagId,
            Name = "Personal",
            Color = "#FF654321"
        });

        cache.InvalidateToken(token);
        var updated = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(1, updated.Tags);
        MSTestAssert.AreEqual("Personal", updated.Tags[0].Name);
        MSTestAssert.AreEqual("#FF654321", updated.Tags[0].Color);

        await tagService.DeletePasswordTagAsync(token, tagId);

        cache.InvalidateToken(token);
        var deleted = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.IsEmpty(deleted.Tags);
    }


    [TestMethod]
    public async Task DeletePasswordTag_RemovesTagIdFromSavedPasswords()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var tagService = host.Services.GetRequiredService<IUserPasswordTagService>();
        var passwordService = host.Services.GetRequiredService<IUserPasswordsService>();
        var cache = host.Services.GetRequiredService<IDataCachingService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("tags-password"));

        await tagService.AddPasswordTagAsync(token, new NewPasswordTagRequest
        {
            Name = "Work",
            Color = "#FF123456"
        });

        var tagId = (await passwordService.GetSavedPasswordsAsync(token)).Tags[0].Id;
        await passwordService.AddNewPasswordAsync(token, new NewPasswordRequest
        {
            Name = "Email",
            Password = Encoding.UTF8.GetBytes("secret"),
            TagIds = [tagId]
        });

        cache.InvalidateToken(token);
        var beforeDelete = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(1, beforeDelete.Passwords[0].TagIds);
        MSTestAssert.AreEqual(tagId, beforeDelete.Passwords[0].TagIds[0]);

        await tagService.DeletePasswordTagAsync(token, tagId);

        cache.InvalidateToken(token);
        var afterDelete = await passwordService.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(1, afterDelete.Passwords);
        MSTestAssert.IsEmpty(afterDelete.Passwords[0].TagIds);
    }


    [TestMethod]
    public async Task PasswordTagEndpoints_WireToService()
    {
        using var host = new BackendTestHost();

        var auth = host.Services.GetRequiredService<IAuthService>();
        var endpoints = host.Services.GetRequiredService<IEndpoints>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("tags-endpoints"));

        await endpoints.AddPasswordTagAsync(token, new NewPasswordTagRequest
        {
            Name = "Important",
            Color = "#FFABCDEF"
        });

        var saved = await endpoints.GetSavedPasswordsAsync(token);
        MSTestAssert.HasCount(1, saved.Tags);
        MSTestAssert.AreEqual("Important", saved.Tags[0].Name);

        await endpoints.UpdatePasswordTagAsync(token, new UpdatePasswordTagRequest
        {
            Id = saved.Tags[0].Id,
            Name = "Updated"
        });

        var updated = await endpoints.GetSavedPasswordsAsync(token);
        MSTestAssert.AreEqual("Updated", updated.Tags[0].Name);

        await endpoints.DeletePasswordTagAsync(token, updated.Tags[0].Id);

        var deleted = await endpoints.GetSavedPasswordsAsync(token);
        MSTestAssert.IsEmpty(deleted.Tags);
    }
}

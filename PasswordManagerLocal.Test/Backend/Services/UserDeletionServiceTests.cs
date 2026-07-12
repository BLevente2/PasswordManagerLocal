using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Test.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class UserDeletionServiceTests
{
    [TestMethod]
    public async Task DeleteUserByToken_RemovesUser()
    {
        using var host = new BackendTestHost();
        var auth = host.Services.GetRequiredService<IAuthService>();
        var sessions = host.Services.GetRequiredService<IUserSessionService>();
        var lookup = host.Services.GetRequiredService<IUserLookupService>();
        var deletion = host.Services.GetRequiredService<IUserDeletionService>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("deleted_user"));
        var uid = sessions.GetUidFromToken(token);

        await deletion.DeleteUserByTokenAsync(token);

        MSTestAssert.IsFalse(await lookup.UserExistsAsync(uid));
    }
}

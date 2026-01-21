using global::PasswordManagerLocalTest.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using System.Linq;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class RememberMeServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task SetRememberMe_ThenInitialize_ReturnsTokenWithKey()
    {
        using var host = new BackendTestHost();

        var auth = (IAuthService)host.Services.GetRequiredService(typeof(IAuthService));
        var remember = (IRememberMeService)host.Services.GetRequiredService(typeof(IRememberMeService));
        var keys = (IKeyVaultService)host.Services.GetRequiredService(typeof(IKeyVaultService));
        var tokens = (ITokenService)host.Services.GetRequiredService(typeof(ITokenService));
        var repo = (IUserRepository)host.Services.GetRequiredService(typeof(IUserRepository));

        var reg = host.CreateValidRegistrationRequest("carol");
        reg.RememberMe = false;

        var token = await auth.RegisterAsync(reg);

        MSTestAssert.AreNotEqual(Guid.Empty, token);
        MSTestAssert.IsTrue(tokens.TryGetUid(token, out var uid));
        MSTestAssert.AreNotEqual(Guid.Empty, uid);

        await remember.SetRememberMeAsync(token, true);

        var users = await repo.ListAllAsync();
        var carol = users.Single(u => u.UId == uid);
        MSTestAssert.IsNotNull(carol.SavedKey);
        MSTestAssert.IsNotEmpty(carol.SavedKey);

        var issued = await remember.InicializeAllRememberMeAsync();

        MSTestAssert.HasCount(1, issued);

        var issuedToken = issued[0];
        MSTestAssert.AreNotEqual(Guid.Empty, issuedToken);
        MSTestAssert.IsTrue(tokens.Validate(issuedToken));
        MSTestAssert.IsTrue(keys.HasUserKey(issuedToken));

        MSTestAssert.IsTrue(tokens.TryGetUid(issuedToken, out var uid2));
        MSTestAssert.AreEqual(uid, uid2);
    }
}
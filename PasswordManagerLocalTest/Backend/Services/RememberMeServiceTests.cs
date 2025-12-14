using global::PasswordManagerLocalTest.TestInfrastructure;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;

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

        Assert.AreNotEqual(Guid.Empty, token);
        Assert.IsTrue(tokens.TryGetUid(token, out var uid));
        Assert.AreNotEqual(Guid.Empty, uid);

        await remember.SetRememberMeAsync(token, true);

        var users = await repo.ListAllAsync();
        var carol = users.Single(u => u.UId == uid);
        Assert.IsNotNull(carol.SavedKey);
        Assert.IsTrue(carol.SavedKey.Length > 0);

        var issued = await remember.InicializeAllRememberMeAsync();

        Assert.AreEqual(1, issued.Count);

        var issuedToken = issued[0];
        Assert.AreNotEqual(Guid.Empty, issuedToken);
        Assert.IsTrue(tokens.Validate(issuedToken));
        Assert.IsTrue(keys.HasUserKey(issuedToken));

        Assert.IsTrue(tokens.TryGetUid(issuedToken, out var uid2));
        Assert.AreEqual(uid, uid2);
    }
}
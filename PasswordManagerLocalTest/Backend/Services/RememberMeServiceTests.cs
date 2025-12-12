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

        await remember.SetRememberMeAsync(token, true);

        var users = await repo.ListAllAsync();
        var carol = users.Single(u => u.UId != Guid.Empty);
        Assert.IsNotNull(carol.SavedKey);
        Assert.IsTrue(carol.SavedKey.Length > 0);

        var issued = await remember.InicializeAllRememberMeAsync();

        Assert.AreEqual(1, issued.Count);
        Assert.IsTrue(tokens.Validate(issued[0]));
        Assert.IsTrue(keys.HasUserKey(issued[0]));
    }
}
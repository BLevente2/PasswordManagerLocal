using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Services;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class TokenServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void Issue_ThenValidate_IsTrue()
    {
        ITokenService svc = new TokenService();
        var token = svc.Issue();
        Assert.IsTrue(svc.Validate(token));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void Validate_InvalidToken_IsFalse()
    {
        ITokenService svc = new TokenService();
        Assert.IsFalse(svc.Validate("not-base64"));
        Assert.IsFalse(svc.Validate(""));
        Assert.IsFalse(svc.Validate(" "));
    }
}

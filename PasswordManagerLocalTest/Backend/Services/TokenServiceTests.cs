using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Services;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class TokenServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void Issue_ThenValidate_AndUidLookup_IsTrue()
    {
        ITokenService svc = new TokenService();

        var uid = Guid.NewGuid();
        var token = svc.Issue(uid);

        Assert.IsTrue(svc.Validate(token));

        Assert.IsTrue(svc.TryGetUid(token, out var uid2));
        Assert.AreEqual(uid, uid2);

        Assert.AreEqual(uid, svc.GetUidOrThrow(token));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void Revoke_RemovesToken()
    {
        ITokenService svc = new TokenService();

        var uid = Guid.NewGuid();
        var token = svc.Issue(uid);

        Assert.IsTrue(svc.Validate(token));
        Assert.IsTrue(svc.Revoke(token));
        Assert.IsFalse(svc.Validate(token));
        Assert.IsFalse(svc.TryGetUid(token, out _));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void Validate_EmptyOrUnknownToken_IsFalse()
    {
        ITokenService svc = new TokenService();

        Assert.IsFalse(svc.Validate(Guid.Empty));
        Assert.IsFalse(svc.Validate(Guid.NewGuid()));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void TryGetUid_EmptyOrUnknownToken_IsFalse()
    {
        ITokenService svc = new TokenService();

        Assert.IsFalse(svc.TryGetUid(Guid.Empty, out var uid1));
        Assert.AreEqual(Guid.Empty, uid1);

        Assert.IsFalse(svc.TryGetUid(Guid.NewGuid(), out var uid2));
        Assert.AreEqual(Guid.Empty, uid2);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void Revoke_EmptyOrUnknownToken_IsFalse()
    {
        ITokenService svc = new TokenService();

        Assert.IsFalse(svc.Revoke(Guid.Empty));
        Assert.IsFalse(svc.Revoke(Guid.NewGuid()));
    }
}
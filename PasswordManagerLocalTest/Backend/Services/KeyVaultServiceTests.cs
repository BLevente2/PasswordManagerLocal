using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Services;
using System.Text;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class KeyVaultServiceTests
{
    private static Guid NewValidToken()
    {
        ITokenService tokens = new TokenService();
        return tokens.Issue(Guid.NewGuid());
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void Set_TryGet_Invalidate_Works()
    {
        IKeyVaultService vault = new KeyVaultService();

        var token = NewValidToken();
        var salt = Hashing.GenerateSalt();

        using var key = EncryptionKey.FromPassword(Encoding.UTF8.GetBytes("P@ssw0rd12345678"), salt);
        vault.SetUserKey(token, key, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.IsTrue(vault.HasUserKey(token));

        Assert.IsTrue(vault.TryGetEncryptionKey(token, out var k2));
        k2.Dispose();

        vault.InvalidateToken(token);
        Assert.IsFalse(vault.HasUserKey(token));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void ExpiredKey_IsRejected()
    {
        IKeyVaultService vault = new KeyVaultService();

        var token = NewValidToken();
        var salt = Hashing.GenerateSalt();

        using var key = EncryptionKey.FromPassword(Encoding.UTF8.GetBytes("P@ssw0rd12345678"), salt);
        vault.SetUserKey(token, key, DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.IsFalse(vault.HasUserKey(token));
        Assert.IsFalse(vault.TryGetEncryptionKey(token, out _));
    }
}
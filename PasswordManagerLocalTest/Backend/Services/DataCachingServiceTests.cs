using global::PasswordManagerLocalTest.TestInfrastructure;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models.Encrypted;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class DataCachingServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task InvalidateToken_ForcesReload()
    {
        using var host = new BackendTestHost();

        var tokens = (ITokenService)host.Services.GetRequiredService(typeof(ITokenService));
        var cache = (IDataCachingService)host.Services.GetRequiredService(typeof(IDataCachingService));

        var uid = Guid.NewGuid();
        var token = tokens.Issue(uid);

        var calls = 0;

        Task<UserData?> Loader()
        {
            calls++;

            var ud = new UserData
            {
                UId = Guid.NewGuid(),
                Username = "u",
                FirstName = "f",
                LastName = "l",
                Email = "e@e.com",
                RegistrationDate = DateTime.UtcNow,
                LastLoginDate = DateTime.UtcNow
            };
            ud.GenerateIntegrityHash();

            return Task.FromResult<UserData?>(ud);
        }

        var a = await cache.GetOrLoadUserDataAsync(token, Loader);
        var b = await cache.GetOrLoadUserDataAsync(token, Loader);

        Assert.IsNotNull(a);
        Assert.IsNotNull(b);
        Assert.AreEqual(1, calls);

        cache.InvalidateToken(token);

        var c = await cache.GetOrLoadUserDataAsync(token, Loader);

        Assert.IsNotNull(c);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void InvalidateGroup_DisposesGroupDataOnRemove()
    {
        using var host = new BackendTestHost();

        var tokens = (ITokenService)host.Services.GetRequiredService(typeof(ITokenService));
        var cache = (IDataCachingService)host.Services.GetRequiredService(typeof(IDataCachingService));

        var token = tokens.Issue(Guid.NewGuid());
        var groupId = Guid.NewGuid();

        var pwBytes = new byte[] { 1, 2, 3, 4, 5 };

        var pw = new SecurePassword
        {
            Id = Guid.NewGuid(),
            Name = "n",
            Description = "d",
            Color = "#FFFFD700",
            Password = pwBytes,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
        pw.GenerateIntegrityHash();

        var gd = new GroupData
        {
            Id = Guid.NewGuid(),
            Name = "g",
            Description = "desc",
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            Passwords = [pw]
        };
        gd.GenerateIntegrityHash();

        cache.SetGroupData(token, groupId, gd);
        cache.InvalidateGroup(token, groupId);

        static bool IsZeroed(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] != 0)
                    return false;
            return true;
        }

        var disposed = SpinWait.SpinUntil(
            () => gd.Id == Guid.Empty && pw.Id == Guid.Empty && IsZeroed(pwBytes),
            TimeSpan.FromSeconds(2));

        Assert.IsTrue(disposed);

        Assert.AreEqual(Guid.Empty, gd.Id);
        Assert.AreEqual(string.Empty, gd.Name);
        Assert.AreEqual(0, gd.Passwords.Count);

        Assert.AreEqual(Guid.Empty, pw.Id);
        Assert.IsTrue(IsZeroed(pwBytes));
    }
}
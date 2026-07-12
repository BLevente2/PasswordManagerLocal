using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Test.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class UserDataPersistenceValidatorTests
{
    [TestMethod]
    public async Task EnsureUserDataBundleCanBePersisted_DuplicateDeviceNames_Throws()
    {
        using var host = new BackendTestHost();
        var auth = host.Services.GetRequiredService<IAuthService>();
        var lookup = host.Services.GetRequiredService<IUserLookupService>();
        var reader = host.Services.GetRequiredService<IUserDataReaderService>();
        var validator = host.Services.GetRequiredService<IUserDataPersistenceValidator>();

        var token = await auth.RegisterAsync(host.CreateValidRegistrationRequest("validator_user"));
        var user = await lookup.GetAndVerifyUserAsync(token);
        var bundle = await reader.GetLoadAndVerifyUserDataBundleAsync(token, user: user);
        var existing = bundle.UserDevicesData.Devices.Single();
        bundle.UserDevicesData.Devices.Add(new UserDeviceData
        {
            Id = Guid.NewGuid(),
            Name = existing.Name,
            LinkedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        });

        MSTestAssert.ThrowsExactly<InvalidOperationException>(
            () => validator.EnsureUserDataBundleCanBePersisted(bundle, user));
    }
}

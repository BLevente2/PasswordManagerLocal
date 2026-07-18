using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Test.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class EphemeralSyncVersionClockServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    public void Next_WithInjectedIdentity_UsesExactLocalInstallation()
    {
        var identity = new FakeDeviceIdentityService
        {
            LocalDeviceId = Guid.NewGuid(),
            OriginInstanceId = Guid.NewGuid(),
            IsInitialized = true
        };
        var clock = new EphemeralSyncVersionClockService(identity);

        var stamp = clock.Next();

        MSTestAssert.AreEqual(identity.LocalDeviceId, stamp.OriginDeviceId);
        MSTestAssert.AreEqual(identity.OriginInstanceId, stamp.OriginInstanceId);
    }
}

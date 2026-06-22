using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Services;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalTest.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class IncomingDeltaApplierServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task Apply_DelegatesDelta_AndReturnsAppliedTimestamp()
    {
        var networkDeltas = new FakeNetworkDeltaService { ApplyResult = 123456789 };
        var service = new IncomingDeltaApplierService(networkDeltas);
        var delta = new NetworkDelta { Ts = 42 };

        var result = await service.ApplyAsync(delta);

        MSTestAssert.AreEqual(123456789L, result);
        MSTestAssert.AreSame(delta, networkDeltas.LastApplied);
    }
}

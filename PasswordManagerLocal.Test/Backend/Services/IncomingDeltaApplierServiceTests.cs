using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Test.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class IncomingDeltaApplierServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task Apply_DelegatesDelta_AndReturnsAppliedTimestamp()
    {
        var networkDeltas = new FakeNetworkDeltaService { ApplyResult = new NetworkDeltaApplyResult(123456789) };
        var service = new IncomingDeltaApplierService(networkDeltas);
        var delta = new NetworkDelta { Ts = 42 };

        var result = await service.ApplyAsync(delta);

        MSTestAssert.AreEqual(123456789L, result.AppliedTimestamp);
        MSTestAssert.AreSame(delta, networkDeltas.LastApplied);
    }
}

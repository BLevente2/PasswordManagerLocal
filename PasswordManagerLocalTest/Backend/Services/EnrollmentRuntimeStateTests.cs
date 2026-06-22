using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Services;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class EnrollmentRuntimeStateTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void ActivateAndDeactivate_AreIdempotent()
    {
        var state = new EnrollmentRuntimeState();

        MSTestAssert.IsFalse(state.IsActive);

        state.Activate();
        state.Activate();
        MSTestAssert.IsTrue(state.IsActive);

        state.Deactivate();
        state.Deactivate();
        MSTestAssert.IsFalse(state.IsActive);
    }
}

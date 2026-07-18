using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Sync;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Sync;

[TestClass]
public sealed class UserSnapshotReceiptProtocolTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void ReceiptStateValues_MatchProtobufContract()
    {
        MSTestAssert.AreEqual(
            (int)UserSnapshotReceiptState.StoredMergedReceipt,
            (int)UserSnapshotReceiptStateProto.UserSnapshotReceiptStoredMergedReceipt);
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Services;
using PasswordManagerLocalTest.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class SyncServiceTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task NeedsSync_EnqueuesExactModelChange()
    {
        var queue = new FakeSyncQueueService();
        var service = new SyncService(queue);
        var modelId = Guid.NewGuid();

        await service.NeedsSyncAsync(modelId, SyncModelType.Group, SyncChangeType.Deleted);

        MSTestAssert.HasCount(1, queue.EnqueuedItems);
        var item = queue.EnqueuedItems[0];
        MSTestAssert.AreEqual(modelId, item.ModelId);
        MSTestAssert.AreEqual(SyncModelType.Group, item.ModelType);
        MSTestAssert.AreEqual(SyncChangeType.Deleted, item.ChangeType);
    }
}

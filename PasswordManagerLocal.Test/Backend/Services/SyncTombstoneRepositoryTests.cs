using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Test.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Services;

[TestClass]
public sealed class SyncTombstoneRepositoryTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task UpsertAsync_WhenLimitReached_KeepsNewestTombstones()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var startTs = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();

        for (var i = 0; i <= TombstoneConstants.MaxSyncTombstones; i++)
        {
            await database.Tombstones.UpsertAsync(Guid.NewGuid(), SyncModelType.Group, startTs + i);
        }

        await database.Db.SaveChangesAsync();

        var tombstones = await database.Db.SyncTombstones
            .OrderBy(tombstone => tombstone.DeletedAtTs)
            .ToListAsync();

        MSTestAssert.HasCount(TombstoneConstants.MaxSyncTombstones, tombstones);
        MSTestAssert.AreEqual(startTs + 1, tombstones.First().DeletedAtTs);
        MSTestAssert.AreEqual(startTs + TombstoneConstants.MaxSyncTombstones, tombstones.Last().DeletedAtTs);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Integration")]
    public async Task UpsertAsync_SameTombstoneBeforeSave_UpdatesTrackedTombstone()
    {
        await using var database = await SqliteIntegrationTestDatabase.CreateAsync();
        var modelId = Guid.NewGuid();
        var olderTs = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeMilliseconds();
        var newerTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await database.Tombstones.UpsertAsync(modelId, SyncModelType.User, olderTs);
        await database.Tombstones.UpsertAsync(modelId, SyncModelType.User, newerTs);
        await database.Db.SaveChangesAsync();

        var tombstone = await database.Db.SyncTombstones.SingleAsync();
        MSTestAssert.AreEqual(modelId, tombstone.ModelId);
        MSTestAssert.AreEqual(SyncModelType.User, tombstone.ModelType);
        MSTestAssert.AreEqual(newerTs, tombstone.DeletedAtTs);
    }

}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Utils;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.Backend.Utils;

[TestClass]
public sealed class TombstoneCleanupUtilTests
{
    [TestMethod]
    public void CleanupExpiredUserDataTombstones_PreservesLongOfflineDeletionKnowledge()
    {
        var now = DateTimeOffset.UtcNow;
        var oldDateTime = now.AddYears(-5).UtcDateTime;
        var oldDateTimeOffset = now.AddYears(-5);
        var passwordId = Guid.NewGuid();
        var colorId = Guid.NewGuid();
        var tagId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var bundle = new UserDataBundle
        {
            UserData = new UserData { UId = Guid.NewGuid() },
            GeneralUserData = new GeneralUserData(),
            UserPasswordsData = new UserPasswordsData
            {
                DeletedPasswords = [new DeletedPasswordData { Id = passwordId, DeletedAt = oldDateTime }],
                DeletedCustomColors = [new DeletedCustomUserColorData { Id = colorId, DeletedAt = oldDateTime }],
                DeletedTags = [new DeletedPasswordTagData { Id = tagId, DeletedAt = oldDateTime }]
            },
            UserDevicesData = new UserDevicesData
            {
                DeletedDevices = [new DeletedUserDeviceData { Id = deviceId, DeletedAt = oldDateTimeOffset }]
            }
        };

        var modifiedBlobs = TombstoneCleanupUtil.CleanupExpiredUserDataTombstones(bundle, now);

        MSTestAssert.AreEqual(UserDataBlobKind.None, modifiedBlobs);
        MSTestAssert.IsTrue(bundle.UserPasswordsData.DeletedPasswords.Any(deleted => deleted.Id == passwordId));
        MSTestAssert.IsTrue(bundle.UserPasswordsData.DeletedCustomColors.Any(deleted => deleted.Id == colorId));
        MSTestAssert.IsTrue(bundle.UserPasswordsData.DeletedTags.Any(deleted => deleted.Id == tagId));
        MSTestAssert.IsTrue(bundle.UserDevicesData.DeletedDevices.Any(deleted => deleted.Id == deviceId));
    }

    [TestMethod]
    public void AddOrUpdateDeletedPasswordAndPasswordTag_WhenLegacyLimitReached_PreservesAllTombstones()
    {
        var now = DateTime.UtcNow;
        var passwords = new UserPasswordsData();
        var oldestPasswordId = FillDeletedPasswordsToLimit(passwords, now);
        var newPasswordId = Guid.NewGuid();
        TombstoneCleanupUtil.AddOrUpdateDeletedPassword(passwords, newPasswordId, now, Stamp(now));

        MSTestAssert.HasCount(TombstoneConstants.MaxUserDataTombstonesPerList + 1, passwords.DeletedPasswords);
        MSTestAssert.IsTrue(passwords.DeletedPasswords.Any(deleted => deleted.Id == oldestPasswordId));
        MSTestAssert.IsTrue(passwords.DeletedPasswords.Any(deleted => deleted.Id == newPasswordId));

        var oldestTagId = FillDeletedPasswordTagsToLimit(passwords, now);
        var newTagId = Guid.NewGuid();
        TombstoneCleanupUtil.AddOrUpdateDeletedPasswordTag(passwords, newTagId, now, Stamp(now));

        MSTestAssert.HasCount(TombstoneConstants.MaxUserDataTombstonesPerList + 1, passwords.DeletedTags);
        MSTestAssert.IsTrue(passwords.DeletedTags.Any(deleted => deleted.Id == oldestTagId));
        MSTestAssert.IsTrue(passwords.DeletedTags.Any(deleted => deleted.Id == newTagId));
    }

    private static Guid FillDeletedPasswordsToLimit(UserPasswordsData passwords, DateTime now)
    {
        var oldestId = Guid.NewGuid();
        TombstoneCleanupUtil.AddOrUpdateDeletedPassword(passwords, oldestId, now.AddDays(-1), Stamp(now.AddDays(-1)));
        for (var i = 1; i < TombstoneConstants.MaxUserDataTombstonesPerList; i++)
            TombstoneCleanupUtil.AddOrUpdateDeletedPassword(passwords, Guid.NewGuid(), now.AddMinutes(-i), Stamp(now.AddMinutes(-i), i));
        return oldestId;
    }

    private static Guid FillDeletedPasswordTagsToLimit(UserPasswordsData passwords, DateTime now)
    {
        var oldestId = Guid.NewGuid();
        TombstoneCleanupUtil.AddOrUpdateDeletedPasswordTag(passwords, oldestId, now.AddDays(-1), Stamp(now.AddDays(-1)));
        for (var i = 1; i < TombstoneConstants.MaxUserDataTombstonesPerList; i++)
            TombstoneCleanupUtil.AddOrUpdateDeletedPasswordTag(passwords, Guid.NewGuid(), now.AddMinutes(-i), Stamp(now.AddMinutes(-i), i));
        return oldestId;
    }
    private static SyncVersionStamp Stamp(DateTime value, long logical = 0) => new()
    {
        PhysicalTimeUnixMilliseconds = new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
        LogicalCounter = logical,
        OriginDeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        OriginInstanceId = Guid.Parse("22222222-2222-2222-2222-222222222222")
    };

}

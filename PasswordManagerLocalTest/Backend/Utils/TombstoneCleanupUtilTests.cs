using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Utils;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Utils;

[TestClass]
public sealed class TombstoneCleanupUtilTests
{
    [TestMethod]
    public void CleanupExpiredUserDataTombstones_RemovesOnlyExpiredTombstones()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredDateTime = now.AddMonths(-TombstoneConstants.TombstoneRetentionMonths).AddSeconds(-1).UtcDateTime;
        var keptDateTime = now.AddMonths(-TombstoneConstants.TombstoneRetentionMonths).AddSeconds(1).UtcDateTime;
        var expiredDateTimeOffset = now.AddMonths(-TombstoneConstants.TombstoneRetentionMonths).AddSeconds(-1);
        var keptDateTimeOffset = now.AddMonths(-TombstoneConstants.TombstoneRetentionMonths).AddSeconds(1);

        var expiredPasswordId = Guid.NewGuid();
        var keptPasswordId = Guid.NewGuid();
        var expiredColorId = Guid.NewGuid();
        var keptColorId = Guid.NewGuid();
        var expiredDeviceId = Guid.NewGuid();
        var keptDeviceId = Guid.NewGuid();
        var bundle = new UserDataBundle
        {
            UserData = new UserData { UId = Guid.NewGuid() },
            GeneralUserData = new GeneralUserData(),
            UserPasswordsData = new UserPasswordsData
            {
                DeletedPasswords =
                [
                    new DeletedPasswordData { Id = expiredPasswordId, DeletedAt = expiredDateTime },
                    new DeletedPasswordData { Id = keptPasswordId, DeletedAt = keptDateTime }
                ],
                DeletedCustomColors =
                [
                    new DeletedCustomUserColorData { Id = expiredColorId, DeletedAt = expiredDateTime },
                    new DeletedCustomUserColorData { Id = keptColorId, DeletedAt = keptDateTime }
                ]
            },
            UserDevicesData = new UserDevicesData
            {
                DeletedDevices =
                [
                    new DeletedUserDeviceData { Id = expiredDeviceId, DeletedAt = expiredDateTimeOffset },
                    new DeletedUserDeviceData { Id = keptDeviceId, DeletedAt = keptDateTimeOffset }
                ]
            }
        };

        var modifiedBlobs = TombstoneCleanupUtil.CleanupExpiredUserDataTombstones(bundle, now);

        MSTestAssert.AreEqual(UserDataBlobKind.Passwords | UserDataBlobKind.Devices, modifiedBlobs);
        MSTestAssert.IsFalse(bundle.UserPasswordsData.DeletedPasswords.Any(deleted => deleted.Id == expiredPasswordId));
        MSTestAssert.IsTrue(bundle.UserPasswordsData.DeletedPasswords.Any(deleted => deleted.Id == keptPasswordId));
        MSTestAssert.IsFalse(bundle.UserPasswordsData.DeletedCustomColors.Any(deleted => deleted.Id == expiredColorId));
        MSTestAssert.IsTrue(bundle.UserPasswordsData.DeletedCustomColors.Any(deleted => deleted.Id == keptColorId));
        MSTestAssert.IsFalse(bundle.UserDevicesData.DeletedDevices.Any(deleted => deleted.Id == expiredDeviceId));
        MSTestAssert.IsTrue(bundle.UserDevicesData.DeletedDevices.Any(deleted => deleted.Id == keptDeviceId));
    }


    [TestMethod]
    public void AddOrUpdateDeletedPassword_WhenLimitReached_KeepsNewTombstoneAndRemovesOldest()
    {
        var now = DateTime.UtcNow;
        var passwords = new UserPasswordsData();
        var oldestId = Guid.NewGuid();
        TombstoneCleanupUtil.AddOrUpdateDeletedPassword(passwords, oldestId, now.AddDays(-1));

        for (var i = 1; i < TombstoneConstants.MaxUserDataTombstonesPerList; i++)
            TombstoneCleanupUtil.AddOrUpdateDeletedPassword(passwords, Guid.NewGuid(), now.AddMinutes(-i));

        var newId = Guid.NewGuid();
        TombstoneCleanupUtil.AddOrUpdateDeletedPassword(passwords, newId, now);

        MSTestAssert.HasCount(TombstoneConstants.MaxUserDataTombstonesPerList, passwords.DeletedPasswords);
        MSTestAssert.IsFalse(passwords.DeletedPasswords.Any(deleted => deleted.Id == oldestId));
        MSTestAssert.IsTrue(passwords.DeletedPasswords.Any(deleted => deleted.Id == newId));
    }
}

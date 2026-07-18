using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Utils;

public static class TombstoneCleanupUtil
{
    // Tombstones are retained conservatively until causal stability can be proven from merged-revision knowledge.
    // Time-based and fixed-count cleanup would allow a long-offline device to resurrect deleted data.
    public static UserDataBlobKind CleanupExpiredUserDataTombstones(UserDataBundle bundle, DateTimeOffset utcNow) =>
        UserDataBlobKind.None;

    public static bool CleanupExpiredUserPasswordsDataTombstones(UserPasswordsData passwords, DateTimeOffset utcNow) => false;
    public static bool CleanupExpiredUserDevicesDataTombstones(UserDevicesData devices, DateTimeOffset utcNow) => false;
    public static bool EnforceUserPasswordsDataTombstoneLimits(UserPasswordsData passwords) => false;
    public static bool EnforceUserDevicesDataTombstoneLimits(UserDevicesData devices) => false;
    public static bool EnforceDeletedPasswordTombstoneLimit(List<DeletedPasswordData> tombstones, Guid? protectedId = null) => false;
    public static bool EnforceDeletedCustomUserColorTombstoneLimit(List<DeletedCustomUserColorData> tombstones, Guid? protectedId = null) => false;
    public static bool EnforceDeletedPasswordTagTombstoneLimit(List<DeletedPasswordTagData> tombstones, Guid? protectedId = null) => false;
    public static bool EnforceDeletedUserDeviceTombstoneLimit(List<DeletedUserDeviceData> tombstones, Guid? protectedId = null) => false;


    public static void AddOrUpdateDeletedPassword(UserPasswordsData passwords, Guid passwordId, DateTime deletedAt, SyncVersionStamp version)
    {
        deletedAt = UtcDateTimeUtil.ToUtc(deletedAt);

        var tombstone = passwords.DeletedPasswords.FirstOrDefault(deleted => deleted.Id == passwordId);
        if (tombstone is null)
        {
            tombstone = new DeletedPasswordData { Id = passwordId };
            passwords.DeletedPasswords.Add(tombstone);
        }

        SyncVersionStampComparer.Validate(version);
        tombstone.DeletedAt = deletedAt;
        tombstone.Version = version;
        tombstone.GenerateIntegrityHash();
        EnforceDeletedPasswordTombstoneLimit(passwords.DeletedPasswords, passwordId);
    }


    public static void AddOrUpdateDeletedCustomUserColor(UserPasswordsData passwords, Guid customUserColorId, DateTime deletedAt, SyncVersionStamp version)
    {
        deletedAt = UtcDateTimeUtil.ToUtc(deletedAt);

        var tombstone = passwords.DeletedCustomColors.FirstOrDefault(deleted => deleted.Id == customUserColorId);
        if (tombstone is null)
        {
            tombstone = new DeletedCustomUserColorData { Id = customUserColorId };
            passwords.DeletedCustomColors.Add(tombstone);
        }

        SyncVersionStampComparer.Validate(version);
        tombstone.DeletedAt = deletedAt;
        tombstone.Version = version;
        tombstone.GenerateIntegrityHash();
        EnforceDeletedCustomUserColorTombstoneLimit(passwords.DeletedCustomColors, customUserColorId);
    }


    public static void AddOrUpdateDeletedPasswordTag(UserPasswordsData passwords, Guid passwordTagId, DateTime deletedAt, SyncVersionStamp version)
    {
        deletedAt = UtcDateTimeUtil.ToUtc(deletedAt);

        var tombstone = passwords.DeletedTags.FirstOrDefault(deleted => deleted.Id == passwordTagId);
        if (tombstone is null)
        {
            tombstone = new DeletedPasswordTagData { Id = passwordTagId };
            passwords.DeletedTags.Add(tombstone);
        }

        SyncVersionStampComparer.Validate(version);
        tombstone.DeletedAt = deletedAt;
        tombstone.Version = version;
        tombstone.GenerateIntegrityHash();
        EnforceDeletedPasswordTagTombstoneLimit(passwords.DeletedTags, passwordTagId);
    }


    public static void AddOrUpdateDeletedUserDevice(UserDevicesData devices, Guid deviceId, DateTimeOffset deletedAt, SyncVersionStamp version)
    {
        deletedAt = UtcDateTimeUtil.ToUtc(deletedAt);

        var tombstone = devices.DeletedDevices.FirstOrDefault(deleted => deleted.Id == deviceId);
        if (tombstone is null)
        {
            tombstone = new DeletedUserDeviceData { Id = deviceId };
            devices.DeletedDevices.Add(tombstone);
        }

        SyncVersionStampComparer.Validate(version);
        tombstone.DeletedAt = deletedAt;
        tombstone.Version = version;
        tombstone.GenerateIntegrityHash();
        EnforceDeletedUserDeviceTombstoneLimit(devices.DeletedDevices, deviceId);
    }

}

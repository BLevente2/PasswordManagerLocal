using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Models.Encrypted;

namespace PasswordManagerLocalBackend.Utils;

public static class TombstoneCleanupUtil
{
    public static UserDataBlobKind CleanupExpiredUserDataTombstones(UserDataBundle bundle, DateTimeOffset utcNow)
    {
        var modifiedBlobs = UserDataBlobKind.None;

        if (CleanupExpiredUserPasswordsDataTombstones(bundle.UserPasswordsData, utcNow))
            modifiedBlobs |= UserDataBlobKind.Passwords;

        if (CleanupExpiredUserDevicesDataTombstones(bundle.UserDevicesData, utcNow))
            modifiedBlobs |= UserDataBlobKind.Devices;

        return modifiedBlobs;
    }


    public static bool CleanupExpiredUserPasswordsDataTombstones(UserPasswordsData passwords, DateTimeOffset utcNow)
    {
        var cutoff = utcNow.ToUniversalTime().AddMonths(-TombstoneConstants.TombstoneRetentionMonths);
        var changed = false;
        changed |= RemoveExpired(passwords.DeletedPasswords, deleted => ToUtcDateTimeOffset(deleted.DeletedAt), cutoff);
        changed |= RemoveExpired(passwords.DeletedCustomColors, deleted => ToUtcDateTimeOffset(deleted.DeletedAt), cutoff);
        changed |= EnforceUserPasswordsDataTombstoneLimits(passwords);
        return changed;
    }


    public static bool CleanupExpiredUserDevicesDataTombstones(UserDevicesData devices, DateTimeOffset utcNow)
    {
        var cutoff = utcNow.ToUniversalTime().AddMonths(-TombstoneConstants.TombstoneRetentionMonths);
        var changed = false;
        changed |= RemoveExpired(devices.DeletedDevices, deleted => deleted.DeletedAt.ToUniversalTime(), cutoff);
        changed |= EnforceUserDevicesDataTombstoneLimits(devices);
        return changed;
    }


    public static bool EnforceUserPasswordsDataTombstoneLimits(UserPasswordsData passwords)
    {
        var changed = false;
        changed |= EnforceDeletedPasswordTombstoneLimit(passwords.DeletedPasswords);
        changed |= EnforceDeletedCustomUserColorTombstoneLimit(passwords.DeletedCustomColors);
        return changed;
    }


    public static bool EnforceUserDevicesDataTombstoneLimits(UserDevicesData devices) =>
        EnforceDeletedUserDeviceTombstoneLimit(devices.DeletedDevices);


    public static bool EnforceDeletedPasswordTombstoneLimit(List<DeletedPasswordData> tombstones, Guid? protectedId = null) =>
        TrimOldest(tombstones, deleted => ToUtcDateTimeOffset(deleted.DeletedAt), deleted => deleted.Id, protectedId);


    public static bool EnforceDeletedCustomUserColorTombstoneLimit(List<DeletedCustomUserColorData> tombstones, Guid? protectedId = null) =>
        TrimOldest(tombstones, deleted => ToUtcDateTimeOffset(deleted.DeletedAt), deleted => deleted.Id, protectedId);


    public static bool EnforceDeletedUserDeviceTombstoneLimit(List<DeletedUserDeviceData> tombstones, Guid? protectedId = null) =>
        TrimOldest(tombstones, deleted => deleted.DeletedAt.ToUniversalTime(), deleted => deleted.Id, protectedId);


    public static void AddOrUpdateDeletedPassword(UserPasswordsData passwords, Guid passwordId, DateTime deletedAt)
    {
        var tombstone = passwords.DeletedPasswords.FirstOrDefault(deleted => deleted.Id == passwordId);
        if (tombstone is null)
        {
            tombstone = new DeletedPasswordData { Id = passwordId };
            passwords.DeletedPasswords.Add(tombstone);
        }

        if (deletedAt > tombstone.DeletedAt)
            tombstone.DeletedAt = deletedAt;

        tombstone.GenerateIntegrityHash();
        EnforceDeletedPasswordTombstoneLimit(passwords.DeletedPasswords, passwordId);
    }


    public static void AddOrUpdateDeletedCustomUserColor(UserPasswordsData passwords, Guid customUserColorId, DateTime deletedAt)
    {
        var tombstone = passwords.DeletedCustomColors.FirstOrDefault(deleted => deleted.Id == customUserColorId);
        if (tombstone is null)
        {
            tombstone = new DeletedCustomUserColorData { Id = customUserColorId };
            passwords.DeletedCustomColors.Add(tombstone);
        }

        if (deletedAt > tombstone.DeletedAt)
            tombstone.DeletedAt = deletedAt;

        tombstone.GenerateIntegrityHash();
        EnforceDeletedCustomUserColorTombstoneLimit(passwords.DeletedCustomColors, customUserColorId);
    }


    public static void AddOrUpdateDeletedUserDevice(UserDevicesData devices, Guid deviceId, DateTimeOffset deletedAt)
    {
        var tombstone = devices.DeletedDevices.FirstOrDefault(deleted => deleted.Id == deviceId);
        if (tombstone is null)
        {
            tombstone = new DeletedUserDeviceData { Id = deviceId };
            devices.DeletedDevices.Add(tombstone);
        }

        if (deletedAt > tombstone.DeletedAt)
            tombstone.DeletedAt = deletedAt;

        tombstone.GenerateIntegrityHash();
        EnforceDeletedUserDeviceTombstoneLimit(devices.DeletedDevices, deviceId);
    }


    private static bool RemoveExpired<T>(
        List<T> tombstones,
        Func<T, DateTimeOffset> deletedAtSelector,
        DateTimeOffset cutoff) where T : IDisposable
    {
        var expired = tombstones
            .Where(tombstone => deletedAtSelector(tombstone) < cutoff)
            .ToList();
        if (expired.Count == 0)
            return false;

        foreach (var tombstone in expired)
        {
            tombstones.Remove(tombstone);
            tombstone.Dispose();
        }

        return true;
    }


    private static bool TrimOldest<T>(
        List<T> tombstones,
        Func<T, DateTimeOffset> deletedAtSelector,
        Func<T, Guid> idSelector,
        Guid? protectedId = null) where T : IDisposable
    {
        var max = TombstoneConstants.MaxUserDataTombstonesPerList;
        if (max < 1)
            max = 1;

        var overflow = tombstones.Count - max;
        if (overflow <= 0)
            return false;

        var removed = tombstones
            .Where(tombstone => !protectedId.HasValue || idSelector(tombstone) != protectedId.Value)
            .OrderBy(deletedAtSelector)
            .ThenBy(idSelector)
            .Take(overflow)
            .ToList();
        if (removed.Count == 0)
            return false;

        foreach (var tombstone in removed)
        {
            tombstones.Remove(tombstone);
            tombstone.Dispose();
        }

        return true;
    }


    private static DateTimeOffset ToUtcDateTimeOffset(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTimeOffset(utc);
    }
}

using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using static PasswordManagerLocal.Backend.Constants.PasswordConstants;
using static PasswordManagerLocal.Backend.Constants.TombstoneConstants;
using static PasswordManagerLocal.Backend.Utils.DataValidationUtil;

namespace PasswordManagerLocal.Backend.Services;

/// <summary>
/// Validates structural limits and cross-reference invariants before encrypted user data is persisted.
/// </summary>
public sealed class UserDataPersistenceValidator : IUserDataPersistenceValidator
{
    public void EnsureUserDataCanBePersisted(UserData userData, User user)
    {
        if (userData.UId == Guid.Empty || userData.UId != user.UId)
            throw new InvalidOperationException("Refusing to persist invalid user data.");

        if (userData.GeneralUserDataKey.Length == 0 ||
            userData.UserPasswordsDataKey.Length == 0 ||
            userData.UserDevicesDataKey.Length == 0 ||
            userData.GeneralUserDataIntegrityHash.Length != Hashing.SHA256HashSizeInBytes ||
            userData.UserPasswordsDataIntegrityHash.Length != Hashing.SHA256HashSizeInBytes ||
            userData.UserDevicesDataIntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
            throw new InvalidOperationException("Refusing to persist incomplete user data.");
    }

    public void EnsureUserDataBundleCanBePersisted(UserDataBundle bundle, User user)
    {
        EnsureUserDataCanBePersisted(bundle.UserData, user);
        EnsurePasswordDataCanBePersisted(bundle.UserPasswordsData);
        EnsureUserDeviceDataCanBePersisted(bundle.UserDevicesData);
        EnsureCustomColorsCanBePersisted(bundle.UserPasswordsData);
        EnsurePasswordTagsCanBePersisted(bundle.UserPasswordsData);
        EnsurePasswordTagReferencesCanBePersisted(bundle.UserPasswordsData);
    }

    private static void EnsurePasswordDataCanBePersisted(UserPasswordsData passwordsData)
    {
        if (passwordsData.PasswordKey.Length == 0)
            throw new InvalidOperationException("Refusing to persist incomplete user data.");

        if (passwordsData.Passwords.Count > MaxNumberOfPasswords)
            throw new InvalidOperationException("Refusing to persist too many passwords.");

        if (passwordsData.CustomColors.Count > MaxNumberOfCustomUserColors)
            throw new InvalidOperationException("Refusing to persist too many custom colors.");

        if (passwordsData.Tags.Count > MaxNumberOfPasswordTags)
            throw new InvalidOperationException("Refusing to persist too many password tags.");

        if (passwordsData.DeletedPasswords.Count > MaxUserDataTombstonesPerList ||
            passwordsData.DeletedCustomColors.Count > MaxUserDataTombstonesPerList ||
            passwordsData.DeletedTags.Count > MaxUserDataTombstonesPerList)
            throw new InvalidOperationException("Refusing to persist too many user data tombstones.");
    }

    private static void EnsureUserDeviceDataCanBePersisted(UserDevicesData? userDevicesData)
    {
        if (userDevicesData is null)
            throw new InvalidOperationException("Refusing to persist incomplete user data.");

        if (userDevicesData.DeletedDevices.Count > MaxUserDataTombstonesPerList)
            throw new InvalidOperationException("Refusing to persist too many user data tombstones.");

        if (userDevicesData.Devices.Any(device =>
                device.Id == Guid.Empty ||
                device.LinkedAt == default ||
                !IsValidUserDeviceName(device.Name)))
            throw new InvalidOperationException("Refusing to persist invalid device data.");

        if (HasDuplicates(userDevicesData.Devices, device => device.Id))
            throw new InvalidOperationException("Refusing to persist duplicate device data.");

        if (HasDuplicates(userDevicesData.Devices, device => device.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to persist duplicate device names.");
    }

    private static void EnsureCustomColorsCanBePersisted(UserPasswordsData passwordsData)
    {
        if (passwordsData.CustomColors.Any(color =>
                color.Id == Guid.Empty ||
                !IsValidARGBColor(color.ColorCode) ||
                !IsValidCustomUserColorName(color.ColorName)))
            throw new InvalidOperationException("Refusing to persist invalid custom color data.");

        if (HasDuplicates(passwordsData.CustomColors, color => color.Id))
            throw new InvalidOperationException("Refusing to persist duplicate custom color data.");

        if (HasDuplicates(passwordsData.CustomColors, color => color.ColorCode.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to persist duplicate custom color codes.");

        var namedColors = passwordsData.CustomColors.Where(color => color.ColorName is not null);
        if (HasDuplicates(namedColors, color => color.ColorName!.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to persist duplicate custom color names.");
    }

    private static void EnsurePasswordTagsCanBePersisted(UserPasswordsData passwordsData)
    {
        if (passwordsData.Tags.Any(tag =>
                tag.Id == Guid.Empty ||
                !IsValidPasswordTagName(tag.Name) ||
                !IsValidARGBColor(tag.Color)))
            throw new InvalidOperationException("Refusing to persist invalid password tag data.");

        if (HasDuplicates(passwordsData.Tags, tag => tag.Id))
            throw new InvalidOperationException("Refusing to persist duplicate password tag data.");

        if (HasDuplicates(passwordsData.Tags, tag => tag.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to persist duplicate password tag names.");
    }

    private static void EnsurePasswordTagReferencesCanBePersisted(UserPasswordsData passwordsData)
    {
        var existingTagIds = passwordsData.Tags.Select(tag => tag.Id).ToHashSet();
        var hasInvalidReferences = passwordsData.Passwords.Any(password =>
            password.TagIds.Any(tagId => tagId == Guid.Empty || !existingTagIds.Contains(tagId)) ||
            password.TagIds.Distinct().Count() != password.TagIds.Count);

        if (hasInvalidReferences)
            throw new InvalidOperationException("Refusing to persist invalid password tag references.");
    }

    private static bool HasDuplicates<TItem, TKey>(
        IEnumerable<TItem> items,
        Func<TItem, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null) =>
        items.GroupBy(keySelector, comparer).Any(group => group.Count() != 1);
}

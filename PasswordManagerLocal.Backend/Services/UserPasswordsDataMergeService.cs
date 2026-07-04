using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Utils;
using static PasswordManagerLocal.Backend.Constants.DataLengthConstants;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Projections;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Utils.DataCodec;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserPasswordsDataMergeService : IUserPasswordsDataMergeService
{
    public bool Merge(UserPasswordsData local, UserPasswordsData incoming)
    {
        if (!local.PasswordKey.SequenceEqual(incoming.PasswordKey))
            return false;

        var passwordEntriesChanged = MergePasswordEntriesForSync(local, incoming);
        var customColorsChanged = MergeCustomColorsForSync(local, incoming);
        var tagsChanged = MergePasswordTagsForSync(local, incoming);
        var tagReferencesChanged = RemoveInvalidPasswordTagReferencesForSync(local);
        return passwordEntriesChanged || customColorsChanged || tagsChanged || tagReferencesChanged;
    }


    private bool MergePasswordEntriesForSync(UserPasswordsData local, UserPasswordsData incoming)
    {
        var changed = false;
        var localPasswords = local.Passwords.ToDictionary(password => password.Id);
        var incomingPasswords = incoming.Passwords.ToDictionary(password => password.Id);
        var localDeleted = local.DeletedPasswords.ToDictionary(deleted => deleted.Id);
        var incomingDeleted = incoming.DeletedPasswords.ToDictionary(deleted => deleted.Id);
        var ids = localPasswords.Keys
            .Concat(incomingPasswords.Keys)
            .Concat(localDeleted.Keys)
            .Concat(incomingDeleted.Keys)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var mergedPasswords = new List<SecurePassword>();
        var mergedDeleted = new List<DeletedPasswordData>();
        foreach (var id in ids)
        {
            localPasswords.TryGetValue(id, out var localPassword);
            incomingPasswords.TryGetValue(id, out var incomingPassword);
            localDeleted.TryGetValue(id, out var localDeletion);
            incomingDeleted.TryGetValue(id, out var incomingDeletion);

            var newestPassword = NewerPassword(localPassword, incomingPassword);
            var newestDeletion = NewerDeletedPassword(localDeletion, incomingDeletion);
            var passwordTime = newestPassword?.LastUpdatedAt ?? UtcDateTimeUtil.MinDateTime;
            var deletionTime = newestDeletion?.DeletedAt ?? UtcDateTimeUtil.MinDateTime;

            if (newestDeletion is not null && deletionTime >= passwordTime)
            {
                mergedDeleted.Add(newestDeletion);
                if (localPassword is not null || !ReferenceEquals(localDeletion, newestDeletion))
                    changed = true;
                continue;
            }

            if (newestPassword is not null)
            {
                mergedPasswords.Add(newestPassword);
                if (!ReferenceEquals(localPassword, newestPassword) || localDeletion is not null)
                    changed = true;
            }
        }

        changed |= local.Passwords.Count != mergedPasswords.Count || local.DeletedPasswords.Count != mergedDeleted.Count;
        if (!changed)
            return TombstoneCleanupUtil.EnforceDeletedPasswordTombstoneLimit(local.DeletedPasswords);

        DisposeItemsNotKept(local.Passwords, mergedPasswords);
        DisposeItemsNotKept(local.DeletedPasswords, mergedDeleted);
        local.Passwords = mergedPasswords.OrderBy(password => password.Name, StringComparer.OrdinalIgnoreCase).ThenBy(password => password.Id).ToList();
        local.DeletedPasswords = mergedDeleted.OrderBy(deleted => deleted.DeletedAt).ThenBy(deleted => deleted.Id).ToList();
        TombstoneCleanupUtil.EnforceDeletedPasswordTombstoneLimit(local.DeletedPasswords);
        return true;
    }


    private bool MergeCustomColorsForSync(UserPasswordsData local, UserPasswordsData incoming)
    {
        var localColors = local.CustomColors.ToDictionary(color => color.Id);
        var incomingColors = incoming.CustomColors.ToDictionary(color => color.Id);
        var localDeleted = local.DeletedCustomColors.ToDictionary(deleted => deleted.Id);
        var incomingDeleted = incoming.DeletedCustomColors.ToDictionary(deleted => deleted.Id);
        var ids = GetCustomColorMergeIds(localColors, incomingColors, localDeleted, incomingDeleted);

        var changed = false;
        var mergedColors = new List<CustomUserColor>();
        var mergedDeleted = new List<DeletedCustomUserColorData>();
        foreach (var id in ids)
            changed |= MergeCustomColorEntryForSync(
                id, localColors, incomingColors, localDeleted, incomingDeleted, mergedColors, mergedDeleted);

        return ApplyMergedCustomColorsForSync(local, mergedColors, mergedDeleted, changed);
    }


    private List<Guid> GetCustomColorMergeIds(
        Dictionary<Guid, CustomUserColor> localColors,
        Dictionary<Guid, CustomUserColor> incomingColors,
        Dictionary<Guid, DeletedCustomUserColorData> localDeleted,
        Dictionary<Guid, DeletedCustomUserColorData> incomingDeleted) =>
        localColors.Keys
            .Concat(incomingColors.Keys)
            .Concat(localDeleted.Keys)
            .Concat(incomingDeleted.Keys)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();


    private bool MergeCustomColorEntryForSync(
        Guid id,
        Dictionary<Guid, CustomUserColor> localColors,
        Dictionary<Guid, CustomUserColor> incomingColors,
        Dictionary<Guid, DeletedCustomUserColorData> localDeleted,
        Dictionary<Guid, DeletedCustomUserColorData> incomingDeleted,
        List<CustomUserColor> mergedColors,
        List<DeletedCustomUserColorData> mergedDeleted)
    {
        localColors.TryGetValue(id, out var localColor);
        incomingColors.TryGetValue(id, out var incomingColor);
        localDeleted.TryGetValue(id, out var localDeletion);
        incomingDeleted.TryGetValue(id, out var incomingDeletion);

        var newestColor = NewerCustomColor(localColor, incomingColor);
        var newestDeletion = NewerDeletedCustomColor(localDeletion, incomingDeletion);
        if (ShouldKeepCustomColorDeletion(newestColor, newestDeletion))
            return AddMergedCustomColorDeletion(localColor, localDeletion, newestDeletion!, mergedDeleted);

        return newestColor is not null
            && AddMergedCustomColor(localColor, localDeletion, newestColor, mergedColors);
    }


    private bool ShouldKeepCustomColorDeletion(
        CustomUserColor? newestColor,
        DeletedCustomUserColorData? newestDeletion)
    {
        var colorTime = newestColor?.LastUpdatedAt ?? UtcDateTimeUtil.MinDateTime;
        var deletionTime = newestDeletion?.DeletedAt ?? UtcDateTimeUtil.MinDateTime;
        return newestDeletion is not null && deletionTime >= colorTime;
    }


    private bool AddMergedCustomColorDeletion(
        CustomUserColor? localColor,
        DeletedCustomUserColorData? localDeletion,
        DeletedCustomUserColorData newestDeletion,
        List<DeletedCustomUserColorData> mergedDeleted)
    {
        mergedDeleted.Add(newestDeletion);
        return localColor is not null || !ReferenceEquals(localDeletion, newestDeletion);
    }


    private bool AddMergedCustomColor(
        CustomUserColor? localColor,
        DeletedCustomUserColorData? localDeletion,
        CustomUserColor newestColor,
        List<CustomUserColor> mergedColors)
    {
        var normalizedCode = NormalizeCustomColorCodeForSync(newestColor.ColorCode);
        var normalizedName = NormalizeCustomColorNameForSync(newestColor.ColorName);
        var changed = newestColor.ColorCode != normalizedCode || newestColor.ColorName != normalizedName;

        newestColor.ColorCode = normalizedCode;
        newestColor.ColorName = normalizedName;
        mergedColors.Add(newestColor);
        return changed || !ReferenceEquals(localColor, newestColor) || localDeletion is not null;
    }


    private bool ApplyMergedCustomColorsForSync(
        UserPasswordsData local,
        List<CustomUserColor> mergedColors,
        List<DeletedCustomUserColorData> mergedDeleted,
        bool changed)
    {
        var deduplicatedColors = ResolveDuplicateCustomColorsForSync(mergedColors);
        changed |= deduplicatedColors.Count != mergedColors.Count;
        changed |= local.CustomColors.Count != deduplicatedColors.Count || local.DeletedCustomColors.Count != mergedDeleted.Count;
        if (!changed)
            return TombstoneCleanupUtil.EnforceDeletedCustomUserColorTombstoneLimit(local.DeletedCustomColors);

        DisposeItemsNotKept(local.CustomColors, deduplicatedColors);
        DisposeItemsNotKept(local.DeletedCustomColors, mergedDeleted);
        local.CustomColors = deduplicatedColors
            .OrderBy(color => color.ColorName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(color => color.ColorCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(color => color.Id)
            .ToList();
        local.DeletedCustomColors = mergedDeleted.OrderBy(deleted => deleted.DeletedAt).ThenBy(deleted => deleted.Id).ToList();
        TombstoneCleanupUtil.EnforceDeletedCustomUserColorTombstoneLimit(local.DeletedCustomColors);
        return true;
    }


    private bool MergePasswordTagsForSync(UserPasswordsData local, UserPasswordsData incoming)
    {
        var localTags = local.Tags.ToDictionary(tag => tag.Id);
        var incomingTags = incoming.Tags.ToDictionary(tag => tag.Id);
        var localDeleted = local.DeletedTags.ToDictionary(deleted => deleted.Id);
        var incomingDeleted = incoming.DeletedTags.ToDictionary(deleted => deleted.Id);
        var ids = GetPasswordTagMergeIds(localTags, incomingTags, localDeleted, incomingDeleted);

        var changed = false;
        var mergedTags = new List<PasswordTag>();
        var mergedDeleted = new List<DeletedPasswordTagData>();
        foreach (var id in ids)
            changed |= MergePasswordTagEntryForSync(
                id, localTags, incomingTags, localDeleted, incomingDeleted, mergedTags, mergedDeleted);

        return ApplyMergedPasswordTagsForSync(local, mergedTags, mergedDeleted, changed);
    }


    private List<Guid> GetPasswordTagMergeIds(
        Dictionary<Guid, PasswordTag> localTags,
        Dictionary<Guid, PasswordTag> incomingTags,
        Dictionary<Guid, DeletedPasswordTagData> localDeleted,
        Dictionary<Guid, DeletedPasswordTagData> incomingDeleted) =>
        localTags.Keys
            .Concat(incomingTags.Keys)
            .Concat(localDeleted.Keys)
            .Concat(incomingDeleted.Keys)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();


    private bool MergePasswordTagEntryForSync(
        Guid id,
        Dictionary<Guid, PasswordTag> localTags,
        Dictionary<Guid, PasswordTag> incomingTags,
        Dictionary<Guid, DeletedPasswordTagData> localDeleted,
        Dictionary<Guid, DeletedPasswordTagData> incomingDeleted,
        List<PasswordTag> mergedTags,
        List<DeletedPasswordTagData> mergedDeleted)
    {
        localTags.TryGetValue(id, out var localTag);
        incomingTags.TryGetValue(id, out var incomingTag);
        localDeleted.TryGetValue(id, out var localDeletion);
        incomingDeleted.TryGetValue(id, out var incomingDeletion);

        var newestTag = NewerPasswordTag(localTag, incomingTag);
        var newestDeletion = NewerDeletedPasswordTag(localDeletion, incomingDeletion);
        if (ShouldKeepPasswordTagDeletion(newestTag, newestDeletion))
            return AddMergedPasswordTagDeletion(localTag, localDeletion, newestDeletion!, mergedDeleted);

        return newestTag is not null
            && AddMergedPasswordTag(localTag, localDeletion, newestTag, mergedTags);
    }


    private bool ShouldKeepPasswordTagDeletion(PasswordTag? newestTag, DeletedPasswordTagData? newestDeletion)
    {
        var tagTime = newestTag?.LastUpdatedAt ?? UtcDateTimeUtil.MinDateTime;
        var deletionTime = newestDeletion?.DeletedAt ?? UtcDateTimeUtil.MinDateTime;
        return newestDeletion is not null && deletionTime >= tagTime;
    }


    private bool AddMergedPasswordTagDeletion(
        PasswordTag? localTag,
        DeletedPasswordTagData? localDeletion,
        DeletedPasswordTagData newestDeletion,
        List<DeletedPasswordTagData> mergedDeleted)
    {
        mergedDeleted.Add(newestDeletion);
        return localTag is not null || !ReferenceEquals(localDeletion, newestDeletion);
    }


    private bool AddMergedPasswordTag(
        PasswordTag? localTag,
        DeletedPasswordTagData? localDeletion,
        PasswordTag newestTag,
        List<PasswordTag> mergedTags)
    {
        var normalizedName = NormalizePasswordTagNameForSync(newestTag.Name);
        var normalizedColor = NormalizePasswordTagColorForSync(newestTag.Color);
        var changed = newestTag.Name != normalizedName || newestTag.Color != normalizedColor;

        newestTag.Name = normalizedName;
        newestTag.Color = normalizedColor;
        mergedTags.Add(newestTag);
        return changed || !ReferenceEquals(localTag, newestTag) || localDeletion is not null;
    }


    private bool ApplyMergedPasswordTagsForSync(
        UserPasswordsData local,
        List<PasswordTag> mergedTags,
        List<DeletedPasswordTagData> mergedDeleted,
        bool changed)
    {
        var deduplicatedTags = ResolveDuplicatePasswordTagsForSync(mergedTags);
        changed |= deduplicatedTags.Count != mergedTags.Count;
        changed |= local.Tags.Count != deduplicatedTags.Count || local.DeletedTags.Count != mergedDeleted.Count;
        if (!changed)
            return TombstoneCleanupUtil.EnforceDeletedPasswordTagTombstoneLimit(local.DeletedTags);

        DisposeItemsNotKept(local.Tags, deduplicatedTags);
        DisposeItemsNotKept(local.DeletedTags, mergedDeleted);
        local.Tags = deduplicatedTags
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tag => tag.Id)
            .ToList();
        local.DeletedTags = mergedDeleted.OrderBy(deleted => deleted.DeletedAt).ThenBy(deleted => deleted.Id).ToList();
        TombstoneCleanupUtil.EnforceDeletedPasswordTagTombstoneLimit(local.DeletedTags);
        return true;
    }


    private bool RemoveInvalidPasswordTagReferencesForSync(UserPasswordsData data)
    {
        var validTagIds = data.Tags.Select(tag => tag.Id).ToHashSet();
        var changed = false;

        foreach (var password in data.Passwords)
        {
            var cleanedTagIds = password.TagIds
                .Where(tagId => tagId != Guid.Empty && validTagIds.Contains(tagId))
                .Distinct()
                .Order()
                .ToList();

            if (password.TagIds.SequenceEqual(cleanedTagIds))
                continue;

            password.TagIds = cleanedTagIds;
            password.LastUpdatedAt = DateTime.UtcNow;
            password.GenerateIntegrityHash();
            changed = true;
        }

        return changed;
    }


    private PasswordTag? NewerPasswordTag(PasswordTag? first, PasswordTag? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.LastUpdatedAt > first.LastUpdatedAt)
            return second;
        return first;
    }


    private DeletedPasswordTagData? NewerDeletedPasswordTag(DeletedPasswordTagData? first, DeletedPasswordTagData? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.DeletedAt > first.DeletedAt)
            return second;
        return first;
    }


    private List<PasswordTag> ResolveDuplicatePasswordTagsForSync(List<PasswordTag> tags)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<PasswordTag>();

        foreach (var tag in tags.OrderByDescending(tag => tag.LastUpdatedAt).ThenBy(tag => tag.Id))
        {
            var normalizedName = NormalizePasswordTagNameForSync(tag.Name);
            var normalizedColor = NormalizePasswordTagColorForSync(tag.Color);
            if (normalizedName.Length == 0 || normalizedColor.Length == 0 || usedNames.Contains(normalizedName))
                continue;

            tag.Name = normalizedName;
            tag.Color = normalizedColor;
            usedNames.Add(normalizedName);
            kept.Add(tag);
        }

        foreach (var tag in tags)
        {
            if (!kept.Any(keptTag => ReferenceEquals(keptTag, tag)))
                tag.Dispose();
        }

        return kept;
    }


    private string NormalizePasswordTagNameForSync(string tagName)
    {
        var normalized = tagName.Trim();
        return normalized.Length <= PasswordTagNameMaxLength ? normalized : string.Empty;
    }


    private string NormalizePasswordTagColorForSync(string color)
    {
        var normalized = color.Trim().ToUpperInvariant();
        if (normalized.Length != ARGBColorLength || normalized[0] != '#')
            return string.Empty;

        return uint.TryParse(normalized.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _)
            ? normalized
            : string.Empty;
    }


    private CustomUserColor? NewerCustomColor(CustomUserColor? first, CustomUserColor? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.LastUpdatedAt > first.LastUpdatedAt)
            return second;
        return first;
    }


    private DeletedCustomUserColorData? NewerDeletedCustomColor(DeletedCustomUserColorData? first, DeletedCustomUserColorData? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.DeletedAt > first.DeletedAt)
            return second;
        return first;
    }


    private List<CustomUserColor> ResolveDuplicateCustomColorsForSync(List<CustomUserColor> colors)
    {
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<CustomUserColor>();

        foreach (var color in colors.OrderByDescending(color => color.LastUpdatedAt).ThenBy(color => color.Id))
        {
            var normalizedCode = NormalizeCustomColorCodeForSync(color.ColorCode);
            if (normalizedCode.Length == 0 || usedCodes.Contains(normalizedCode))
                continue;

            var normalizedName = NormalizeCustomColorNameForSync(color.ColorName);
            if (normalizedName is not null && usedNames.Contains(normalizedName))
                continue;

            color.ColorCode = normalizedCode;
            color.ColorName = normalizedName;
            usedCodes.Add(normalizedCode);
            if (normalizedName is not null)
                usedNames.Add(normalizedName);
            kept.Add(color);
        }

        foreach (var color in colors)
        {
            if (!kept.Any(keptColor => ReferenceEquals(keptColor, color)))
                color.Dispose();
        }

        return kept;
    }


    private string NormalizeCustomColorCodeForSync(string colorCode)
    {
        var normalized = colorCode.Trim().ToUpperInvariant();
        if (normalized.Length != ARGBColorLength || normalized[0] != '#')
            return string.Empty;

        return uint.TryParse(normalized.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _)
            ? normalized
            : string.Empty;
    }


    private string? NormalizeCustomColorNameForSync(string? colorName)
    {
        if (colorName is null)
            return null;

        var trimmed = colorName.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }


    private void DisposeItemsNotKept<T>(IEnumerable<T> currentItems, IReadOnlyCollection<T> keptItems) where T : class, IDisposable
    {
        foreach (var current in currentItems)
        {
            if (!keptItems.Any(kept => ReferenceEquals(kept, current)))
                current.Dispose();
        }
    }


    private SecurePassword? NewerPassword(SecurePassword? first, SecurePassword? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.LastUpdatedAt > first.LastUpdatedAt)
            return second;
        return first;
    }


    private DeletedPasswordData? NewerDeletedPassword(DeletedPasswordData? first, DeletedPasswordData? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.DeletedAt > first.DeletedAt)
            return second;
        return first;
    }
}

using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Utils;
using static PasswordManagerLocal.Backend.Constants.PasswordConstants;

namespace PasswordManagerLocal.Backend.Services;

public sealed class PasswordTagService : IPasswordTagService
{
    public IReadOnlyList<PasswordTagInfoResponse> ConvertToPasswordTagInfoResponses(UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        return passwords.Tags
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(tag => tag.Id)
            .Select(PasswordTagInfoResponse.ConvertToPasswordTagInfoResponse)
            .ToList();
    }


    public void AddPasswordTag(NewPasswordTagRequest request, UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        if (passwords.Tags.Count >= MaxNumberOfPasswordTags)
            throw new LimitReachedException(MaxNumberOfPasswordTags, "passwordTag");

        var tagName = NormalizeTagName(request.Name);
        var color = NormalizeColorCode(request.Color);
        ThrowIfPasswordTagNameExists(tagName, passwords);

        var tag = new PasswordTag
        {
            Id = Guid.NewGuid(),
            Name = tagName,
            Color = color,
            LastUpdatedAt = DateTime.UtcNow
        };
        tag.GenerateIntegrityHash();

        passwords.DeletedTags.RemoveAll(deleted => deleted.Id == tag.Id);
        passwords.Tags.Add(tag);
        passwords.GeneratePasswordTagsIntegrityHash();
    }


    public void DeletePasswordTag(Guid passwordTagId, UserPasswordsData passwords)
    {
        using var tag = GetAndVerifyPasswordTagById(passwordTagId, passwords);
        TombstoneCleanupUtil.AddOrUpdateDeletedPasswordTag(passwords, tag.Id, DateTime.UtcNow);
        passwords.Tags.Remove(tag);

        var removedFromAnyPassword = false;
        foreach (var password in passwords.Passwords)
        {
            password.VerifyIntegrity();
            if (password.TagIds.RemoveAll(id => id == passwordTagId) == 0)
                continue;

            removedFromAnyPassword = true;
            password.LastUpdatedAt = DateTime.UtcNow;
            password.GenerateIntegrityHash();
        }

        if (removedFromAnyPassword)
            passwords.GeneratePasswordsIntegrityHash();
        passwords.GeneratePasswordTagsIntegrityHash();
    }


    public void UpdatePasswordTag(UpdatePasswordTagRequest request, UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var tag = GetAndVerifyPasswordTagById(request.Id, passwords);

        if (request.Name is not null)
        {
            var normalizedName = NormalizeTagName(request.Name);
            ThrowIfPasswordTagNameExists(normalizedName, passwords, tag.Id);
            tag.Name = normalizedName;
        }

        if (request.Color is not null)
            tag.Color = NormalizeColorCode(request.Color);

        passwords.DeletedTags.RemoveAll(deleted => deleted.Id == tag.Id);
        tag.LastUpdatedAt = DateTime.UtcNow;
        tag.GenerateIntegrityHash();
        passwords.GeneratePasswordTagsIntegrityHash();
    }


    public PasswordTag GetAndVerifyPasswordTagById(Guid passwordTagId, UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        var tag = passwords.Tags.FirstOrDefault(tag => tag.Id == passwordTagId);
        if (tag is null)
            throw new PasswordTagNotFoundException(passwordTagId);

        tag.VerifyIntegrity();
        return tag;
    }


    private string NormalizeTagName(string name) => name.Trim();


    private string NormalizeColorCode(string colorCode) => colorCode.Trim().ToUpperInvariant();


    private void ThrowIfPasswordTagNameExists(
        string tagName,
        UserPasswordsData passwords,
        Guid? ignoredPasswordTagId = null)
    {
        var normalizedName = NormalizeTagName(tagName);
        var exists = passwords.Tags.Any(tag =>
            (!ignoredPasswordTagId.HasValue || tag.Id != ignoredPasswordTagId.Value)
            && string.Equals(NormalizeTagName(tag.Name), normalizedName, StringComparison.OrdinalIgnoreCase));

        if (exists)
            throw new DuplicatePasswordTagNameException(normalizedName);
    }
}

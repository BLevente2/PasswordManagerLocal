using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;
using static PasswordManagerLocal.Backend.Constants.PasswordConstants;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Services;

public sealed class CustomUserColorService : ICustomUserColorService
{
    public IReadOnlyList<CustomUserColorInfoResponse> ConvertToCustomUserColorInfoResponses(UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        return passwords.CustomColors
            .OrderBy(color => color.ColorName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(color => color.ColorCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(color => color.Id)
            .Select(CustomUserColorInfoResponse.ConvertToCustomUserColorInfoResponse)
            .ToList();
    }


    public void AddCustomUserColor(NewCustomUserColorRequest request, UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        if (passwords.CustomColors.Count >= MaxNumberOfCustomUserColors)
            throw new LimitReachedException(MaxNumberOfCustomUserColors, "customUserColor");

        var colorName = NormalizeOptionalColorName(request.ColorName);
        var colorCode = NormalizeColorCode(request.ColorCode);
        ThrowIfCustomColorNameExists(colorName, passwords);
        ThrowIfCustomColorCodeExists(colorCode, passwords);

        var customColor = new CustomUserColor
        {
            Id = Guid.NewGuid(),
            ColorName = colorName,
            ColorCode = colorCode,
            LastUpdatedAt = DateTime.UtcNow
        };
        customColor.GenerateIntegrityHash();

        passwords.DeletedCustomColors.RemoveAll(deleted => deleted.Id == customColor.Id);
        passwords.CustomColors.Add(customColor);
        passwords.GenerateCustomColorsIntegrityHash();
    }


    public void DeleteCustomUserColor(Guid customUserColorId, UserPasswordsData passwords)
    {
        using var color = GetAndVerifyCustomUserColorById(customUserColorId, passwords);
        TombstoneCleanupUtil.AddOrUpdateDeletedCustomUserColor(passwords, color.Id, DateTime.UtcNow);
        passwords.CustomColors.Remove(color);
        passwords.GenerateCustomColorsIntegrityHash();
    }


    public void UpdateCustomUserColor(UpdateCustomUserColorRequest request, UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var color = GetAndVerifyCustomUserColorById(request.Id, passwords);

        var newColorName = color.ColorName;
        if (request.ClearColorName)
            newColorName = null;
        else if (request.ColorName is not null)
            newColorName = NormalizeOptionalColorName(request.ColorName);

        var newColorCode = color.ColorCode;
        if (request.ColorCode is not null)
            newColorCode = NormalizeColorCode(request.ColorCode);

        ThrowIfCustomColorNameExists(newColorName, passwords, color.Id);
        ThrowIfCustomColorCodeExists(newColorCode, passwords, color.Id);

        color.ColorName = newColorName;
        color.ColorCode = newColorCode;
        passwords.DeletedCustomColors.RemoveAll(deleted => deleted.Id == color.Id);
        color.LastUpdatedAt = DateTime.UtcNow;
        color.GenerateIntegrityHash();
        passwords.GenerateCustomColorsIntegrityHash();
    }


    public CustomUserColor GetAndVerifyCustomUserColorById(Guid customUserColorId, UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        var color = passwords.CustomColors.FirstOrDefault(color => color.Id == customUserColorId);
        if (color is null)
            throw new CustomUserColorNotFoundException(customUserColorId);

        color.VerifyIntegrity();
        return color;
    }


    private string NormalizeColorCode(string colorCode) => colorCode.Trim().ToUpperInvariant();


    private string? NormalizeOptionalColorName(string? colorName)
    {
        if (colorName is null)
            return null;

        var trimmed = colorName.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }


    private void ThrowIfCustomColorNameExists(
        string? colorName,
        UserPasswordsData passwords,
        Guid? ignoredCustomColorId = null)
    {
        if (colorName is null)
            return;

        var normalizedColorName = colorName.Trim();
        var exists = passwords.CustomColors.Any(color =>
            color.ColorName is not null
            && (!ignoredCustomColorId.HasValue || color.Id != ignoredCustomColorId.Value)
            && string.Equals(color.ColorName.Trim(), normalizedColorName, StringComparison.OrdinalIgnoreCase));

        if (exists)
            throw new DuplicateCustomUserColorNameException(normalizedColorName);
    }


    private void ThrowIfCustomColorCodeExists(
        string colorCode,
        UserPasswordsData passwords,
        Guid? ignoredCustomColorId = null)
    {
        var exists = passwords.CustomColors.Any(color =>
            (!ignoredCustomColorId.HasValue || color.Id != ignoredCustomColorId.Value)
            && string.Equals(NormalizeColorCode(color.ColorCode), colorCode, StringComparison.OrdinalIgnoreCase));

        if (exists)
            throw new DuplicateCustomUserColorCodeException(colorCode);
    }
}

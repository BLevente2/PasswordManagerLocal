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


    public void AddCustomUserColor(NewCustomUserColorRequest request, UserPasswordsData passwords) =>
        AddCustomUserColors([request], passwords);


    public void AddCustomUserColors(IReadOnlyList<NewCustomUserColorRequest> requests, UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        if (requests.Count == 0)
            return;

        if (passwords.CustomColors.Count + requests.Count > MaxNumberOfCustomUserColors)
            throw new LimitReachedException(MaxNumberOfCustomUserColors, "customUserColor");

        var normalizedColors = NormalizeAndValidateNewCustomColors(requests, passwords);
        var now = DateTime.UtcNow;

        foreach (var (colorName, colorCode) in normalizedColors)
        {
            var customColor = new CustomUserColor
            {
                Id = Guid.NewGuid(),
                ColorName = colorName,
                ColorCode = colorCode,
                LastUpdatedAt = now
            };
            customColor.GenerateIntegrityHash();
            passwords.CustomColors.Add(customColor);
        }

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


    private List<(string? ColorName, string ColorCode)> NormalizeAndValidateNewCustomColors(
        IReadOnlyList<NewCustomUserColorRequest> requests,
        UserPasswordsData passwords)
    {
        var usedNames = passwords.CustomColors
            .Where(color => color.ColorName is not null)
            .Select(color => color.ColorName!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedCodes = passwords.CustomColors
            .Select(color => NormalizeColorCode(color.ColorCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedColors = new List<(string? ColorName, string ColorCode)>(requests.Count);

        foreach (var request in requests)
        {
            if (!request.Validate(out var errors))
                throw new InvalidInputException(errors);

            var colorName = NormalizeOptionalColorName(request.ColorName);
            var colorCode = NormalizeColorCode(request.ColorCode);

            if (colorName is not null && !usedNames.Add(colorName))
                throw new DuplicateCustomUserColorNameException(colorName);

            if (!usedCodes.Add(colorCode))
                throw new DuplicateCustomUserColorCodeException(colorCode);

            normalizedColors.Add((colorName, colorCode));
        }

        return normalizedColors;
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

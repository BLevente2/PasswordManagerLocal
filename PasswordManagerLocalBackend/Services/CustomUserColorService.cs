using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;
using static PasswordManagerLocalBackend.Constants.PasswordConstants;
using PasswordManagerLocalBackend.Utils;

namespace PasswordManagerLocalBackend.Services;

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

        var colorCode = NormalizeColorCode(request.ColorCode);
        ThrowIfCustomColorCodeExists(colorCode, passwords);

        var customColor = new CustomUserColor
        {
            Id = Guid.NewGuid(),
            ColorName = NormalizeOptionalColorName(request.ColorName),
            ColorCode = colorCode,
            LastUpdatedAt = DateTime.UtcNow
        };
        customColor.GenerateIntegrityHash();

        passwords.DeletedCustomColors.RemoveAll(deleted => deleted.Id == customColor.Id);
        passwords.CustomColors.Add(customColor);
        passwords.GenerateIntegrityHash();
    }


    public void DeleteCustomUserColor(Guid customUserColorId, UserPasswordsData passwords)
    {
        using var color = GetAndVerifyCustomUserColorById(customUserColorId, passwords);
        TombstoneCleanupUtil.AddOrUpdateDeletedCustomUserColor(passwords, color.Id, DateTime.UtcNow);
        passwords.CustomColors.Remove(color);
        passwords.GenerateIntegrityHash();
    }


    public void UpdateCustomUserColor(UpdateCustomUserColorRequest request, UserPasswordsData passwords)
    {
        passwords.VerifyIntegrity();

        if (!request.Validate(out var errors))
            throw new InvalidInputException(errors);

        var color = GetAndVerifyCustomUserColorById(request.Id, passwords);

        if (request.ClearColorName)
            color.ColorName = null;
        else if (request.ColorName is not null)
            color.ColorName = NormalizeOptionalColorName(request.ColorName);

        if (request.ColorCode is not null)
        {
            var normalizedColorCode = NormalizeColorCode(request.ColorCode);
            ThrowIfCustomColorCodeExists(normalizedColorCode, passwords, color.Id);
            color.ColorCode = normalizedColorCode;
        }

        passwords.DeletedCustomColors.RemoveAll(deleted => deleted.Id == color.Id);
        color.LastUpdatedAt = DateTime.UtcNow;
        color.GenerateIntegrityHash();
        passwords.GenerateIntegrityHash();
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


    private static string NormalizeColorCode(string colorCode) => colorCode.Trim().ToUpperInvariant();


    private static string? NormalizeOptionalColorName(string? colorName)
    {
        if (colorName is null)
            return null;

        var trimmed = colorName.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }


    private static void ThrowIfCustomColorCodeExists(
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
